using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Ui.Editor;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The window's side of the settings: its edits, undo and the file changed or broken from outside, on a settings file of
/// the test's own, the window's turn as a queue the test runs, as the window's thread would.
/// </summary>
public sealed class WindowEditorTests : IAsyncDisposable
{
    private readonly SettingsFolder _folder = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _turn = new();
    private readonly Settings _settings;
    private readonly WindowEditor _editor;

    public WindowEditorTests()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        _settings = _folder.Settings();
        Assert.True(_folder.Settled(_settings.SyncAsync).IsCompletedSuccessfully);
        _editor = new WindowEditor(_settings, _turn.Enqueue, _folder.Log, _folder.Clock);
    }

    // The settings file as the window's writes left it.
    private ThermiConfig Written
    {
        get
        {
            Settle();
            return _folder.Read();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _settings.DisposeAsync();
        _folder.Dispose();
    }

    // R40, R41: while the file is broken, the editor knows where and why, and refuses every change, undo and redo included,
    // leaving the file as the hand left it; restoring keeps it beside, under a dated name, and writes the last good settings.
    [Fact]
    public void ABrokenFileAllowsNoChangeUntilTheLastGoodSettingsAreRestored()
    {
        _editor.Edit("own pill", config => ItemEdits.SetOwn(config, Metrics.CpuPower.Id, own: true));
        Settle();
        const string broken = "{\n  \"version\": 2,\n  \"overlays\": [ { ] }\n}";
        _folder.Write(broken);
        _settings.FileChanged();
        Settle();

        var problem = _editor.Problem;
        var refused = _editor.Edit("lock", config => OverlayEdits.SetLocked(config, 0, on: true));
        var refusedStyle = _editor.RenameStyle(new SavedStyles(ConfigStore.StylesFolder(_folder.File)), "a", "b");
        var undo = (_editor.CanUndo, _editor.Undo());
        Settle();
        var text = _folder.Text();
        _editor.Restore();
        Settle();

        Assert.Equal((ConfigProblemKind.Broken, 3L), (problem?.Kind, problem?.Line));
        Assert.Equal((Refusal.SettingsBroken, Refusal.SettingsBroken), (refused.Refused, refusedStyle.Refused));
        Assert.Equal((false, null), undo);
        Assert.Equal(broken, text);
        Assert.Null(_editor.Problem);
        Assert.Contains("config.broken-2026-11-22", string.Join(" ", _folder.Files), StringComparison.Ordinal);
        Assert.True(Item(Written, Metrics.CpuPower.Id).Own);
        Assert.False(Written.Overlays[0].Locked);
    }

    // A lock let go without a write raises no change of the file, so the window reads it again while it cannot use it,
    // as the overlay does, and takes edits again once it can.
    [Fact]
    public void AFileFoundLockedIsReadAgainUntilItCanBeUsed()
    {
        using (new FileStream(_folder.File, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _settings.FileChanged();
            _folder.Pass(ConfigStore.Settle + Settings.Interval);
            Settle();
        }

        var locked = _editor.Problem?.Kind;
        _folder.Pass(Settings.Recheck + ConfigStore.Settle);
        Settle();
        var edited = _editor.Edit("lock", config => OverlayEdits.SetLocked(config, 0, on: true));

        Assert.Equal(ConfigProblemKind.Locked, locked);
        Assert.Null(_editor.Problem);
        Assert.Null(edited.Refused);
        Assert.True(Written.Overlays[0].Locked);
    }

    // R8, R9: a change made outside during a gesture waits for its end, stays when the gesture is undone, and an undo of a
    // field changed by hand since is skipped, and said.
    [Fact]
    public void UndoKeepsWhatChangedOutsideAndSaysWhatItSkipped()
    {
        var order = _editor.Config.Overlays[0].Items.Select(item => item.Id).ToList();
        _editor.BeginGesture();
        _editor.Edit("drag", config => ItemEdits.Move(config, Metrics.RamUsed.Id, 0));
        ByHand(config => Done(ItemEdits.RenamePill(config, Metrics.GpuLoad.Id, "CARD")));
        var during = _editor.Config;
        _editor.EndGesture();
        var after = _editor.Config;
        _editor.Edit("threshold", config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 75, 90));
        ByHand(config => Done(ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 80, 95)));

        _editor.Undo();
        var skipped = _editor.Note;
        _editor.Undo();
        Settle();

        Assert.Equal((null, "CARD"), (Item(during, Metrics.GpuLoad.Id).Label, Item(after, Metrics.GpuLoad.Id).Label));
        Assert.Equal(new EditNote(null, null, Skipped: true), skipped);
        Assert.Null(_editor.Note);
        Assert.Equal(order, _folder.Read().Overlays[0].Items.Select(item => item.Id));
        Assert.Equal(
            ("CARD", new Threshold(80, 95)),
            (Item(_folder.Read(), Metrics.GpuLoad.Id).Label, Item(_folder.Read(), Metrics.CpuTemperature.Id).Threshold));
    }

    [Fact]
    public void ARefusedEditChangesNothingAndSaysWhy()
    {
        _editor.Edit("threshold", config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 90, 80));

        Assert.Equal(Refusal.WarningAboveCritical, _editor.Note?.Refused);
        Assert.False(_editor.CanUndo);
    }

    private static ItemConfig Item(ThermiConfig config, string id) => config.Overlays.SelectMany(overlay => overlay.Items).Single(item => item.Id == id);

    private static ThermiConfig Done(EditResult result)
    {
        Assert.Null(result.Refused);
        return result.Config;
    }

    // A person's edit of the file, which the watcher would report.
    private void ByHand(Func<ThermiConfig, ThermiConfig> change)
    {
        Settle();
        _folder.Write(ThermiConfigJson.Write(change(_folder.Read())));
        _settings.FileChanged();
        Settle();
    }

    // The throttle's waits pass, and the window takes what came meanwhile.
    private void Settle()
    {
        _folder.Pass(Settings.Interval * 3);
        while (_turn.TryDequeue(out var action))
        {
            action();
        }
    }
}
