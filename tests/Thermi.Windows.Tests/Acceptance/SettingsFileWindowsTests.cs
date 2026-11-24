using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// The settings file shared by the overlay and the window: no write loses another's, an editor's save is waited out, a
/// broken file is never written over, and a process knows its own writes.
/// </summary>
public sealed class SettingsFileWindowsTests : IDisposable
{
    private static readonly DragDistance Distance = new(4, 4);

    private static readonly Press AtTheCorner = new(new Position(0, 0), new Position(0, 0));

    private static readonly Func<ThermiConfig, ThermiConfig> Resize = config => OverlayEdits.SetSize(config, 0, 1.5).Config;

    private static readonly Func<ThermiConfig, ThermiConfig> Drag = config => OverlayEdits.Drop(config, 0, new Position(10, 20)).Config;

    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task TwoWritersAtOnceNeverLoseAChange()
    {
        _folder.Write(SettingsFolder.Sized(1));
        var (window, overlay) = (_folder.Store(), _folder.Store());
        await window.ReadAsync();
        await overlay.ReadAsync();

        Task<ConfigWrite> resized, dragged;
        using (var other = MutexHolder.Holding(_folder.MutexName))
        {
            resized = window.WriteAsync(Resize);
            dragged = overlay.WriteAsync(Drag);
            other.Release();
        }

        _folder.RunOut(Task.WhenAll(resized, dragged));
        Assert.Equal(WriteOutcome.Written, (await resized).Outcome);
        Assert.Equal(WriteOutcome.Written, (await dragged).Outcome);
        Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
        Assert.Equal(new Position(10, 20), _folder.Read().Overlays[0].Position);
    }

    [Fact]
    public async Task AReadDuringAWriteIsRetriedNotReportedAsBroken()
    {
        _folder.Write(SettingsFolder.Sized(1.5));
        var started = _folder.Clock.GetTimestamp();
        Task<ConfigRead> read;
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            read = _folder.Store().ReadAsync();
            _folder.Pass(TimeSpan.FromMilliseconds(300));

            Assert.False(read.IsCompleted);
        }

        _folder.RunOut(read);
        var result = await read;

        Assert.Null(result.Problem);
        Assert.Equal(1.5, result.Config.Overlays[0].Size);
        Assert.True(_folder.Clock.GetElapsedTime(started) <= ConfigStore.Settle);
    }

    [Fact]
    public async Task AnEditorsSaveNeverShowsAsDefaults()
    {
        _folder.Write(SettingsFolder.Sized(1.5));
        var settings = await _folder.LoadedAsync();
        var shown = new List<double>();
        settings.Changed += (_, _) => shown.Add(settings.Config.Overlays[0].Size);

        _folder.Write("");
        var emptied = settings.SyncAsync();
        _folder.Pass(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1.5, settings.Config.Overlays[0].Size);
        _folder.Write(SettingsFolder.Sized(1.25));
        _folder.RunOut(emptied);
        await emptied;

        File.Delete(_folder.File);
        var removed = settings.SyncAsync();
        _folder.Pass(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1.25, settings.Config.Overlays[0].Size);
        Assert.False(File.Exists(_folder.File));
        _folder.Write(SettingsFolder.Sized(0.75));
        _folder.RunOut(removed);
        await removed;

        Assert.Equal([1.25, 0.75], shown);
        Assert.Null(settings.Problem);
        Assert.Equal(SettingsFolder.Sized(0.75), _folder.Text());
    }

    [Fact]
    public async Task AMutexLeftByADeadWriterIsAcquired()
    {
        _folder.Write(SettingsFolder.Sized(1));
        await File.WriteAllTextAsync(_folder.File + ".dead-writer.tmp", "half a config", TestContext.Current.CancellationToken);
        MutexHolder.DiesHolding(_folder.MutexName);

        var write = await _folder.Settled(() => _folder.Store().WriteAsync(Resize));

        Assert.Equal(WriteOutcome.Written, write.Outcome);
        Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
        Assert.Equal(["config.json"], _folder.Files);
    }

    [Fact]
    public async Task AWriterWaitsOutAnEditorsSave()
    {
        var store = _folder.Store();
        var editedByHand = ThermiConfigJson.Write(new ThermiConfig { Language = "es" });
        foreach (var unsettle in new Action[] { () => _folder.Write(editedByHand[..40]), () => File.Delete(_folder.File) })
        {
            _folder.Write(SettingsFolder.Sized(1));
            unsettle();

            var write = store.WriteAsync(Resize);
            _folder.Pass(TimeSpan.FromMilliseconds(300));
            Assert.False(write.IsCompleted);
            _folder.Write(editedByHand);

            _folder.RunOut(write);
            Assert.Equal(WriteOutcome.Written, (await write).Outcome);
            Assert.Equal("es", _folder.Read().Language);
            Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
        }
    }

    [Fact]
    public async Task AReaderNeverBlocksAWrite()
    {
        _folder.Write(SettingsFolder.Sized(1));
        ConfigWrite write;
        using (ConfigStore.OpenShared(_folder.File))
        {
            write = await _folder.Settled(() => _folder.Store().WriteAsync(Resize));
        }

        Assert.Equal(WriteOutcome.Written, write.Outcome);
        Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
    }

    [Fact]
    public async Task AWriterNeverOverwritesABrokenFile()
    {
        _folder.Write("{ \"version\": 2, broken }");

        var write = await _folder.Settled(() => _folder.Store().WriteAsync(Resize));

        Assert.Equal(WriteOutcome.Broken, write.Outcome);
        Assert.NotNull(write.Problem);
        Assert.Equal("{ \"version\": 2, broken }", _folder.Text());
        Assert.Equal(["config.json"], _folder.Files);
    }

    /// <summary>What keeps a write from landing for a while.</summary>
    public enum Block
    {
        /// <summary>The file held locked longer than an editor's save.</summary>
        Locked,

        /// <summary>The file read without leave to delete it: the re-read finds it whole, and only the move into place fails.</summary>
        ReadOnlyShare,

        /// <summary>Another process holding the mutex past its timeout.</summary>
        Mutex,
    }

    // The drag waits on screen, leaves no file behind, and lands once the block goes. Waiting for the mutex is no failure;
    // a move that failed is, and so is a lock that outlasts an editor's save, and each is logged once.
    [Theory]
    [InlineData(Block.Locked, 1)]
    [InlineData(Block.ReadOnlyShare, 1)]
    [InlineData(Block.Mutex, 0)]
    public async Task ABlockedWriteIsRetriedUntilItLands(Block block, int logged)
    {
        var settings = await _folder.LoadedAsync();
        var (during, after) = block switch
        {
            Block.Locked => (ConfigStore.MutexTimeout, ConfigStore.Settle + Settings.Backoff + Settings.Interval),
            Block.ReadOnlyShare => (TimeSpan.FromSeconds(1), Settings.Backoff),
            Block.Mutex => (ConfigStore.MutexTimeout * 3, ConfigStore.Poll),
            _ => throw new ArgumentOutOfRangeException(nameof(block)),
        };
        using (Blocking(block))
        {
            Assert.Equal(WriteOutcome.Retry, (await _folder.Settled(() => _folder.Store().WriteAsync(Drag))).Outcome);
            settings.Release(AtTheCorner, new Position(10, 20), Distance);
            _folder.Pass(during);

            Assert.Equal(new Position(10, 20), settings.Config.Overlays[0].Position);
            Assert.Equal(["config.json"], _folder.Files);
        }

        Assert.Null(_folder.Read().Overlays[0].Position);

        _folder.Pass(after);

        Assert.Equal(new Position(10, 20), _folder.Read().Overlays[0].Position);
        Assert.Equal(logged, _folder.Log.Entries.Count);
    }

    private IDisposable Blocking(Block block) => block switch
    {
        Block.Locked => new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None),
        Block.ReadOnlyShare => new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.Read),
        Block.Mutex => MutexHolder.Holding(_folder.MutexName),
        _ => throw new ArgumentOutOfRangeException(nameof(block)),
    };

    [Fact]
    public async Task RestoringKeepsTheBrokenFile()
    {
        _folder.Clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _folder.Write(SettingsFolder.Sized(1.5));
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        await _folder.Settled(settings.SyncAsync);

        var restored = await _folder.Settled(() => _folder.Store().RestoreAsync(settings.Config));

        Assert.NotNull(settings.Problem);
        Assert.Equal(WriteOutcome.Written, restored.Outcome);
        Assert.Equal(SettingsFolder.Sized(1.5), _folder.Text());
        Assert.Equal(["config.broken-2026-11-22-153045.json", "config.json"], _folder.Files.Order(StringComparer.Ordinal));
        Assert.Equal(
            "{ \"version\": 2, broken }",
            await File.ReadAllTextAsync(
                Path.Combine(Path.GetDirectoryName(_folder.File)!, "config.broken-2026-11-22-153045.json"),
                TestContext.Current.CancellationToken));
    }

    // A read-only file takes no restore, retried once a second: each try that fails takes its copy back, so only the one
    // that lands keeps one.
    [Fact]
    public async Task ARestoreThatCannotLandKeepsNoCopyUntilItDoes()
    {
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        await _folder.Settled(settings.SyncAsync);
        File.SetAttributes(_folder.File, FileAttributes.ReadOnly);
        IReadOnlyList<string> whileReadOnly;
        try
        {
            settings.Restore();
            _folder.Pass(TimeSpan.FromSeconds(10));
            whileReadOnly = [.. _folder.Files];
        }
        finally
        {
            File.SetAttributes(_folder.File, FileAttributes.Normal);
        }

        _folder.Pass(Settings.Backoff + Settings.Interval);

        Assert.Equal(["config.json"], whileReadOnly);
        Assert.Equal(2, _folder.Files.Count());
        Assert.Single(_folder.Files, file => file.StartsWith("config.broken-", StringComparison.Ordinal));
        Assert.Null(settings.Problem);
    }

    // A restore asked for while another process holds the mutex, or while the file is locked, finds the file reading well
    // once it can look: fixed by hand, or only unlocked, it is left as it is and shown, and no copy is kept.
    [Fact]
    public async Task ARestoreLeavesAFileThatReadsWellAgain()
    {
        var fixedByHand = SettingsFolder.Sized(1.25);
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        await _folder.Settled(settings.SyncAsync);
        using (var other = MutexHolder.Holding(_folder.MutexName))
        {
            settings.Restore();
            _folder.Pass(Settings.Interval);
            _folder.Write(fixedByHand);
            other.Release();
        }

        _folder.Pass(ConfigStore.MutexTimeout + Settings.Interval);
        var afterTheHand = (_folder.Text(), settings.Problem, settings.Config.Overlays[0].Size);

        _folder.Write(SettingsFolder.Sized(0.75));
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await _folder.Settled(settings.SyncAsync);
            Assert.Equal(ConfigProblemKind.Locked, settings.Problem?.Kind);
            settings.Restore();
            _folder.Pass(ConfigStore.Settle);
        }

        _folder.Pass(ConfigStore.Settle + Settings.Interval);

        Assert.Equal((fixedByHand, null, 1.25), afterTheHand);
        Assert.Equal(SettingsFolder.Sized(0.75), _folder.Text());
        Assert.Null(settings.Problem);
        Assert.Equal(0.75, settings.Config.Overlays[0].Size);
        Assert.Equal(["config.json"], _folder.Files);
    }

    [Fact]
    public async Task ARestoreWaitsOutALockAsAWriteDoes()
    {
        _folder.Write("{ \"version\": 2, broken }");
        Task<ConfigWrite> restore;
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            restore = _folder.Store().RestoreAsync(new ThermiConfig());
            _folder.Pass(ConfigStore.Settle / 2);

            Assert.False(restore.IsCompleted);
        }

        _folder.RunOut(restore);

        Assert.Equal(WriteOutcome.Written, (await restore).Outcome);
        Assert.Equal(2, _folder.Files.Count());
    }

    // A lock that outlasts the wait is Windows refusing the restore: said once, then tried once a second until it lets go.
    // The settings never read the broken file, so no recheck of it runs beside the restore.
    [Fact]
    public async Task ARestoreOverAFileThatStaysLockedIsLoggedOnce()
    {
        _folder.Write(SettingsFolder.Sized(1.5));
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Restore();
            _folder.Pass(TimeSpan.FromSeconds(10));
        }

        _folder.Pass(Settings.Backoff + ConfigStore.Settle + Settings.Interval);

        Assert.IsType<IOException>(Assert.Single(_folder.Log.Entries).Error);
        Assert.Equal(SettingsFolder.Sized(1.5), _folder.Text());
        Assert.Single(_folder.Files, file => file.StartsWith("config.broken-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheWindowsOwnWriteIsNotAnOutsideChange()
    {
        var window = _folder.Store();
        await _folder.Settled(() => window.WriteAsync(Resize));
        var own = await window.ReadAsync();
        _folder.Write(SettingsFolder.Sized(1.25));
        var byHand = await window.ReadAsync();

        var overlay = await _folder.LoadedAsync();
        var changes = 0;
        overlay.Changed += (_, _) => changes++;
        overlay.Release(AtTheCorner, new Position(10, 20), Distance);
        var afterItsWrite = changes;
        _folder.Clock.Advance(Settings.Interval);
        overlay.FileChanged();

        Assert.False(own.Outside);
        Assert.True(byHand.Outside);
        Assert.Equal(1.25, byHand.Config.Overlays[0].Size);
        Assert.Equal(new Position(10, 20), _folder.Read().Overlays[0].Position);
        Assert.Equal(afterItsWrite, changes);
    }

    // As an editor's undo, or the window's, puts back what the window wrote after a change shown in between.
    [Fact]
    public async Task TheWindowsOwnWriteComingBackAfterAChangeIsAnOutsideChange()
    {
        var window = _folder.Store();
        await _folder.Settled(() => window.WriteAsync(Resize));
        var own = _folder.Text();
        _folder.Write(SettingsFolder.Sized(1.25));
        var changed = await window.ReadAsync();

        _folder.Write(own);
        var back = await window.ReadAsync();

        Assert.True(changed.Outside);
        Assert.True(back.Outside);
        Assert.Equal(1.5, back.Config.Overlays[0].Size);
    }
}
