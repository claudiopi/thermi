using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Commands;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>The user's config file: never overwritten while broken, no drag is lost, and a click is never written.</summary>
public sealed class SettingsTests : IDisposable
{
    private static readonly DragDistance Distance = new(4, 4);

    // A press on the overlay at the screen's top-left corner, where the overlay's own corner is too.
    private static readonly Press AtTheCorner = new(new Position(0, 0), new Position(0, 0));

    private readonly SettingsFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AMissingFileIsCreatedWithTheDefaults()
    {
        var settings = await _folder.LoadedAsync();

        Assert.Null(settings.Problem);
        Assert.Equal(ThermiConfigJson.Write(new ThermiConfig()), _folder.Text());
    }

    // As written by hand, so any save, even of the same settings, would show as a change of the text.
    [Fact]
    public async Task AClickOrAPressOnALockedOverlayWritesNothing()
    {
        const string Snapped = """{ "version": 2, "overlays": [ { "snap": { "spot": "taskbar-center" } } ] }""";
        const string Locked = """{ "version": 2, "overlays": [ { "locked": true } ] }""";
        foreach (var (text, released) in new[] { (Snapped, new Position(0, 0)), (Snapped, new Position(4, -4)), (Locked, new Position(300, 300)) })
        {
            _folder.Write(text);
            var settings = await _folder.LoadedAsync();

            settings.Release(AtTheCorner, released, Distance);
            _folder.Pass(Settings.Interval);

            Assert.Equal(text, _folder.Text());
            Assert.Null(settings.Config.Overlays[0].Position);
        }
    }

    // Fixed by hand without the first overlay, so the one dragged comes first: the drag goes with the overlay it moved.
    [Fact]
    public async Task ABrokenFileIsNeverOverwrittenAndTheDragIsSavedOnceItIsFixed()
    {
        _folder.Write("""{ "version": 2, "overlays": [ { "items": [ { "metric": "cpu.load" } ] }, { "items": [ { "metric": "gpu.load" } ] } ] }""");
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"items\": [ broken");
        _folder.Pass(Settings.Interval);
        await _folder.Settled(settings.SyncAsync);

        settings.Release(AtTheCorner, new Position(10, 20), Distance, overlay: 1);
        _folder.Pass(Settings.Interval);

        Assert.NotNull(settings.Problem);
        Assert.Equal(new Position(10, 20), settings.Config.Overlays[1].Position);
        Assert.Equal("{ \"items\": [ broken", _folder.Text());

        _folder.Write("""{ "version": 2, "overlays": [ { "items": [ { "metric": "gpu.load" } ] } ] }""");
        await _folder.Settled(settings.SyncAsync);

        Assert.Null(settings.Problem);
        Assert.Equal(new Position(10, 20), _folder.Read().Overlays[0].Position);
    }

    // While the file is broken the overlay reads it again every second, and the next style must not move each time.
    [Fact]
    public async Task AnEditWhileTheFileIsBrokenIsAppliedOnce()
    {
        StyleConfig[] saved =
        [
            new() { Name = "Big", Preset = StylePreset.Large },
            new() { Name = "Daylight", Pills = PillTheme.Light },
            new() { Name = "Small", Preset = StylePreset.Compact },
        ];
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        await _folder.Settled(settings.SyncAsync);

        settings.Edit(config => OverlayEdits.NextStyle(config, saved));
        var shown = new List<string?>();
        for (var tick = 0; tick < 5; tick++)
        {
            settings.FileChanged();
            _folder.Pass(TimeSpan.FromSeconds(1));
            shown.Add(settings.Config.Overlays[0].Style.Name);
        }

        Assert.NotNull(settings.Problem);
        Assert.Equal(["Big", "Big", "Big", "Big", "Big"], shown);
    }

    // A file Windows will not let thermi replace: the first failure is logged, and the retries slow to one a second.
    [Fact]
    public async Task AWriteThatKeepsFailingIsLoggedOnceAndRetriedOnceASecond()
    {
        var settings = await _folder.LoadedAsync();
        var tries = 0;
        File.SetAttributes(_folder.File, FileAttributes.ReadOnly);
        try
        {
            settings.Edit(config =>
            {
                tries++;
                return OverlayEdits.SetSize(config, 0, 1.5).Config;
            });
            _folder.Pass(TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.SetAttributes(_folder.File, FileAttributes.Normal);
        }

        var whileReadOnly = tries;
        _folder.Pass(Settings.Backoff);

        Assert.Single(_folder.Log.Entries);
        Assert.InRange(whileReadOnly, 10, 20);
        Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
    }

    // Quitting while a write keeps failing, during the write itself or between two tries: nothing more is reported, no
    // try comes after, and a write that fails as quitting begins sets no timer quitting let go, as Windows' own would throw.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QuittingWhileAWriteFailsReportsNothingMoreAndStopsTrying(bool duringTheWrite)
    {
        var settings = await _folder.LoadedAsync(new StrictTime(_folder.Clock));
        var (tries, closed, quit) = (0, 0, false);
        Task? closing = null;
        Task Close()
        {
            return closing ??= settings.DisposeAsync().AsTask();
        }

        ReadOnly(() =>
        {
            settings.Edit(config =>
            {
                tries++;
                if (quit)
                {
                    _ = Close();
                }

                return OverlayEdits.SetSize(config, 0, 1.5).Config;
            });
            _folder.Pass(TimeSpan.FromSeconds(2));
            if (duringTheWrite)
            {
                quit = true;
                _folder.Pass(Settings.Backoff);
            }
            else
            {
                _folder.RunOut(Close());
            }

            closed = tries;
            _folder.Pass(Settings.Backoff * 3);
        });

        Assert.NotNull(closing);
        await closing;
        Assert.Equal(closed, tries);
        Assert.Single(_folder.Log.Entries);
    }

    [Fact]
    public async Task ADragReleasedJustBeforeQuittingIsWritten()
    {
        var settings = await _folder.LoadedAsync();
        settings.Release(AtTheCorner, new Position(10, 20), Distance);
        settings.Release(new Press(new Position(10, 20), new Position(10, 20)), new Position(30, 40), Distance);
        _folder.Pass(Settings.Interval / 2);

        await _folder.Settled(() => settings.DisposeAsync().AsTask());

        Assert.Equal(new Position(30, 40), _folder.Read().Overlays[0].Position);
    }

    // Each press of the next-style hotkey posts the same method: one made while another is being written is still its own.
    [Fact]
    public async Task TheSameEditPostedTwiceDuringOneWriteIsAppliedTwice()
    {
        var settings = await _folder.LoadedAsync();
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            settings.Edit(Grow);
            settings.Edit(Grow);
        }

        _folder.Pass(ConfigStore.Settle + Settings.Interval);

        Assert.Equal(1.5, _folder.Read().Overlays[0].Size);
        Assert.Equal(1.5, settings.Config.Overlays[0].Size);
    }

    // While the file is broken it is read every second, and the overlay registers its hotkeys again on every change.
    [Fact]
    public async Task ReadingTheSameBrokenFileAgainRaisesNothing()
    {
        var settings = await _folder.LoadedAsync();
        _folder.Write("{ \"version\": 2, broken }");
        await _folder.Settled(settings.SyncAsync);
        var changes = 0;
        settings.Changed += (_, _) => changes++;

        ReadAgain(settings, times: 3);
        var whileUnchanged = changes;
        settings.Edit(config => OverlayEdits.SetLocked(config, 0, on: true).Config);
        ReadAgain(settings, times: 3);

        Assert.Equal(0, whileUnchanged);
        Assert.Equal(1, changes);
        Assert.True(settings.Config.Overlays[0].Locked);
    }

    // A picture that fails midway leaves no file that would pass for one; a file the preview could not even open, as a
    // read-only one, is the user's and stays.
    [Fact]
    public async Task APreviewThatFailsLeavesNoPartialPicture()
    {
        var folder = Path.GetDirectoryName(_folder.File)!;
        var (partial, kept) = (Path.Combine(folder, "partial.png"), Path.Combine(folder, "kept.png"));
        await File.WriteAllTextAsync(kept, "the user's", TestContext.Current.CancellationToken);
        File.SetAttributes(kept, FileAttributes.ReadOnly);
        int failedMidway, refused;
        try
        {
            failedMidway = await Preview.WriteAsync(partial, png =>
            {
                png.WriteByte(0x89);
#pragma warning disable CA2201 // What GDI+ throws for a picture it could not write, which the test stands in for.
                throw new System.Runtime.InteropServices.ExternalException("A generic error occurred in GDI+.");
#pragma warning restore CA2201
            });
            refused = await Preview.WriteAsync(kept, _ => { });
        }
        finally
        {
            File.SetAttributes(kept, FileAttributes.Normal);
        }

        Assert.Equal((1, 1), (failedMidway, refused));
        Assert.False(File.Exists(partial));
        Assert.Equal("the user's", await File.ReadAllTextAsync(kept, TestContext.Current.CancellationToken));
    }

    // It paints what the overlay shows, the driver's marker on a CPU value that needs it included.
    [Fact]
    public async Task APreviewReadsTheSettingsWithoutCreatingThem()
    {
        var png = Path.Combine(Path.GetDirectoryName(_folder.File)!, "preview.png");
        var snapshot = TestRecordings.Path(TestRecordings.Admin);

        var painted = await _folder.Settled(() => Preview.RunAsync(new PreviewRequest(snapshot, png, 1, _folder.File), _folder.Clock));

        Assert.Equal(0, painted);
        Assert.True(File.Exists(png));
        Assert.False(File.Exists(_folder.File));
        var driver = Texts.For(null, CultureInfo.CurrentUICulture).Of(OverlayText.Driver);
        Assert.Contains(Preview.Shown(new ThermiConfig(), TestRecordings.Read(TestRecordings.NoAdmin), baseClock: null), item => item.Value == driver);
    }

    [Fact]
    public async Task ReloadsGoThroughTheThrottle()
    {
        var settings = await _folder.LoadedAsync();
        var changes = 0;
        settings.Changed += (_, _) => changes++;

        foreach (var size in new[] { 1.25, 1.5, 1.75 })
        {
            _folder.Write(SettingsFolder.Sized(size));
            settings.FileChanged();
        }

        Assert.Equal(1, changes);
        Assert.Equal(1.25, settings.Config.Overlays[0].Size);

        _folder.Clock.Advance(Settings.Interval);

        Assert.Equal(2, changes);
        Assert.Equal(1.75, settings.Config.Overlays[0].Size);
    }

    // Windows drops a watcher's changes when they come faster than it reads them, and says so only as an error, raised
    // here as Windows raises it: the file is read again, since a change may have been lost, and watched on.
    [Fact]
    public async Task AWatcherThatLostChangesReadsTheFileAgain()
    {
        var settings = await _folder.LoadedAsync();
        _folder.Write(SettingsFolder.Sized(1.5));
        using var watcher = settings.Watch(_folder.Log)!;

        typeof(FileSystemWatcher).GetMethod("OnError", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(watcher, [new ErrorEventArgs(new InternalBufferOverflowException())]);

        Assert.Equal((1.5, true), (settings.Config.Overlays[0].Size, watcher.EnableRaisingEvents));
    }

    private static ThermiConfig Grow(ThermiConfig config) => OverlayEdits.SetSize(config, 0, config.Overlays[0].Size + 0.25).Config;

    private void ReadAgain(Settings settings, int times)
    {
        for (var read = 0; read < times; read++)
        {
            settings.FileChanged();
            _folder.Pass(TimeSpan.FromSeconds(1));
        }
    }

    // A file Windows will not let thermi replace, for the length of the work.
    private void ReadOnly(Action work)
    {
        File.SetAttributes(_folder.File, FileAttributes.ReadOnly);
        try
        {
            work();
        }
        finally
        {
            File.SetAttributes(_folder.File, FileAttributes.Normal);
        }
    }

    /// <summary>The fake clock, with timers that throw once disposed, as those of <see cref="TimeProvider.System"/> do.</summary>
    private sealed class StrictTime(FakeTimeProvider fake) : TimeProvider
    {
        public override long TimestampFrequency => fake.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => fake.LocalTimeZone;

        public override long GetTimestamp() => fake.GetTimestamp();

        public override DateTimeOffset GetUtcNow() => fake.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new Timer(fake.CreateTimer(callback, state, dueTime, period));

        private sealed class Timer(ITimer inner) : ITimer
        {
            private volatile bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return inner.Change(dueTime, period);
            }

            public void Dispose()
            {
                _disposed = true;
                inner.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
