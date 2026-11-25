using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Logging;
using Thermi.Overlay;

namespace Thermi.Tray;

/// <summary>
/// What a row chosen in thermi's menu or an overlay's does: in an overlay's menu or submenu it acts on that overlay, in
/// thermi's menu on all. Each edit is one undoable step, as a desktop gesture is.
/// </summary>
internal sealed class TrayChoices(TrayParts parts, ILogger log)
{
    private Settings Settings => parts.Settings;

    private OverlayActions Actions => parts.Actions;

    public void Choose(MenuRow row, int? menuOverlay)
    {
        var target = row.Overlay ?? menuOverlay;
        switch (row.Entry)
        {
            case MenuEntry.SettingsBroken or MenuEntry.Settings or MenuEntry.EditItem:
                Actions.OpenSettings(target ?? 0, row.Item);
                break;
            case MenuEntry.StartSensors or MenuEntry.TaskManager or MenuEntry.CopyImage:
                Reach(row.Entry, target ?? 0);
                break;
            case MenuEntry.ShowOverlays or MenuEntry.HideOverlays:
                parts.Visibility.HideOverlays = row.Entry == MenuEntry.HideOverlays;
                break;
            case MenuEntry.UndoGesture:
                parts.Overlays[target ?? 0].UndoGesture();
                break;
            case MenuEntry.Separate when row.Item is { } item && (target ?? 0) < parts.Overlays.Count:
                parts.Overlays[target ?? 0].Separate(item);
                break;
            case MenuEntry.ResetMinMax:
                On(target).ToList().ForEach(at => Actions.History.Reset(Settings.Config.Overlays[at]));
                break;
            case MenuEntry.Exit:
                Actions.Exit();
                break;
            case MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay or MenuEntry.ClickThrough or MenuEntry.Lock
                or MenuEntry.Graph or MenuEntry.Vertical or MenuEntry.Look:
                Write(row, EditOf(row, On(target), target ?? 0));
                break;
            case MenuEntry.Separate or MenuEntry.GraphOwn or MenuEntry.Overlay or MenuEntry.Update:
            default:
                break;
        }

        Actions.RequestPaint();
    }

    public IReadOnlyList<StyleConfig> Looks() => new SavedStyles(ConfigStore.StylesFolder(Settings.FilePath)).List();

    private IReadOnlyList<int> On(int? target) =>
        target is { } one ? [one] : [.. Enumerable.Range(0, Math.Min(Settings.Config.Overlays.Count, parts.Overlays.Count))];

    private Func<ThermiConfig, ThermiConfig>? EditOf(MenuRow row, IReadOnlyList<int> on, int at) => row.Entry switch
    {
        MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay => config =>
            on.Aggregate(config, (current, each) => OverlayEdits.SetHidden(current, each, row.Entry == MenuEntry.HideThisOverlay).Config),
        MenuEntry.ClickThrough => config => on.Aggregate(config, (current, each) => OverlayEdits.SetClickThrough(current, each, !row.Checked).Config),
        MenuEntry.Lock => config => on.Aggregate(config, (current, each) => OverlayEdits.SetLocked(current, each, !row.Checked).Config),
        MenuEntry.Graph when row.Graph is { } graph => config => OverlayEdits.SetGraph(config, at, graph).Config,
        MenuEntry.Vertical => config =>
            StyleEdits.Restyle(config, at, style => style with { Orientation = row.Checked ? Orientation.Horizontal : Orientation.Vertical }).Config,
        MenuEntry.Look when (row.Look is null ? new StyleConfig() : Looks().FirstOrDefault(look => look.Name == row.Look)) is { } look => config =>
            StyleEdits.Choose(config, at, look).Config,
        MenuEntry.SettingsBroken or MenuEntry.StartSensors or MenuEntry.ShowOverlays or MenuEntry.UndoGesture or MenuEntry.Settings
            or MenuEntry.TaskManager or MenuEntry.CopyImage or MenuEntry.HideOverlays or MenuEntry.ResetMinMax or MenuEntry.Overlay
            or MenuEntry.Update or MenuEntry.Exit or MenuEntry.EditItem or MenuEntry.Separate or MenuEntry.GraphOwn => null,
        _ => null,
    };

    // Writes only the difference, so what another process wrote meanwhile stays.
    private void Write(MenuRow row, Func<ThermiConfig, ThermiConfig>? edit)
    {
        var before = Settings.Config;
        var after = edit?.Invoke(before) ?? before;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            return;
        }

        Settings.Edit(change);
        if (Menus.UndoOf(row) is { } undo)
        {
            Actions.Gestures?.Add(GestureKind.Menu, before, after, undo);
        }
    }

    private void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.StartSensors)
        {
            _ = Task.Run(StartSensorsAsync, CancellationToken.None);
        }
        else if (entry == MenuEntry.TaskManager)
        {
            parts.Environment.TaskManager();
        }
        else if (overlay < parts.Overlays.Count && parts.Overlays[overlay].Picture() is { } picture)
        {
            using (picture)
            {
                parts.Environment.CopyImage(parts.Overlays[overlay].Hwnd, picture);
            }
        }
    }

    // On a thread of its own: starting a process behind UAC blocks its caller until the prompt closes, and the host's thread
    // must keep painting. The pipe coming back is what shows the service ran.
    private async Task StartSensorsAsync()
    {
        try
        {
            if (await parts.Environment.StartSensors().ConfigureAwait(false) is { } code and not StartSensors.Started)
            {
                log.StartSensorsEnded(code);
            }
        }
#pragma warning disable CA1031 // A start that fails leaves the sensors stopped, which the menu still offers to fix.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.StartSensorsFailed(ex);
        }
    }
}
