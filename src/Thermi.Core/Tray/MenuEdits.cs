using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Core.Tray;

/// <summary>
/// The edit of the settings a row chosen in a menu makes, on the overlays <c>on</c> names: in an overlay's menu or submenu
/// that overlay, in thermi's menu all. Null for a row that is no edit of the settings, as "Settings…" or "Exit".
/// </summary>
public static class MenuEdits
{
    /// <summary>The overlays a row acts on: the one it or its menu names, <paramref name="target"/>, or every one.</summary>
    public static IReadOnlyList<int> On(int? target, int overlays) => target is { } one ? [one] : [.. Enumerable.Range(0, overlays)];

    public static Func<ThermiConfig, ThermiConfig>? EditOf(MenuRow row, IReadOnlyList<int> on, int at, IReadOnlyList<StyleConfig> looks) =>
        row.Entry switch
        {
            MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay => config =>
                on.Aggregate(config, (current, each) => OverlayEdits.SetHidden(current, each, row.Entry == MenuEntry.HideThisOverlay).Config),
            MenuEntry.ClickThrough => config => on.Aggregate(config, (current, each) => OverlayEdits.SetClickThrough(current, each, !row.Checked).Config),
            MenuEntry.Lock => config => on.Aggregate(config, (current, each) => OverlayEdits.SetLocked(current, each, !row.Checked).Config),
            MenuEntry.Graph when row.Graph is { } graph => config => OverlayEdits.SetGraph(config, at, graph).Config,
            MenuEntry.Vertical => config =>
                StyleEdits.Restyle(config, at, style => style with { Orientation = row.Checked ? Orientation.Horizontal : Orientation.Vertical }).Config,
            MenuEntry.Look when (row.Look is null ? new StyleConfig() : looks.FirstOrDefault(look => look.Name == row.Look)) is { } look => config =>
                StyleEdits.Choose(config, at, look).Config,
            MenuEntry.Size or MenuEntry.Bigger or MenuEntry.Smaller when row.Size is { } size => config => OverlayEdits.SetSize(config, at, size).Config,
            MenuEntry.SettingsBroken or MenuEntry.StartSensors or MenuEntry.ShowOverlays or MenuEntry.UndoGesture or MenuEntry.Settings
                or MenuEntry.TaskManager or MenuEntry.CopyImage or MenuEntry.HideOverlays or MenuEntry.ResetMinMax or MenuEntry.Overlay
                or MenuEntry.Update or MenuEntry.Exit or MenuEntry.EditItem or MenuEntry.Separate or MenuEntry.GraphOwn or MenuEntry.SeparateFull
                or MenuEntry.SizeCustom or MenuEntry.SizeOnTaskbar => null,
            _ => null,
        };
}
