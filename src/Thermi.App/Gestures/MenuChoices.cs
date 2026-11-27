using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using ItemHistory = Thermi.Core.Look.ItemHistory;

namespace Thermi.App.Gestures;

/// <summary>What a menu's row reaches past the settings, which each OS's host does its own way.</summary>
internal interface IMenuHost
{
    Settings Settings { get; }

    /// <summary>The last desktop gesture, which a menu's edit becomes too; none while the host offers no undo.</summary>
    GestureUndo? Gestures { get; }

    ItemHistory History { get; }

    /// <summary>How many overlays the host shows now.</summary>
    int Overlays { get; }

    /// <summary>The settings an edit leaves, each overlay it pushed along a taskbar where it was pushed, or why it is refused.</summary>
    EditResult Settle(ThermiConfig before, ThermiConfig after);

    void OpenSettings(int overlay, string? item);

    /// <summary>Opens the window on an overlay's Size row.</summary>
    void OpenSize(int overlay);

    /// <summary>The sensors' start, the system monitor, or an overlay's picture copied, as the desktop has them.</summary>
    void Reach(MenuEntry entry, int overlay);

    void UndoGesture(int overlay);

    /// <summary>Separates the pill of <paramref name="item"/> from overlay <paramref name="overlay"/>, as a drag of it would.</summary>
    void Separate(int overlay, string item);

    void HideOverlays(bool hidden);

    void Exit();

    void RequestPaint();
}

/// <summary>
/// What a row chosen in thermi's menu or an overlay's does, on every OS: in an overlay's menu or submenu it acts on that
/// overlay, in thermi's menu on all. Each edit is one undoable step, as a desktop gesture is.
/// </summary>
internal static class MenuChoices
{
    public static void Choose(IMenuHost host, MenuRow row, int? menuOverlay)
    {
        var target = row.Overlay ?? menuOverlay;
        switch (row.Entry)
        {
            case MenuEntry.SettingsBroken or MenuEntry.Settings or MenuEntry.EditItem:
                host.OpenSettings(target ?? 0, row.Item);
                break;
            case MenuEntry.SizeCustom:
                host.OpenSize(target ?? 0);
                break;
            case MenuEntry.StartSensors or MenuEntry.TaskManager or MenuEntry.CopyImage:
                host.Reach(row.Entry, target ?? 0);
                break;
            case MenuEntry.ShowOverlays or MenuEntry.HideOverlays:
                host.HideOverlays(row.Entry == MenuEntry.HideOverlays);
                break;
            case MenuEntry.UndoGesture:
                host.UndoGesture(target ?? 0);
                break;
            case MenuEntry.Separate when row.Item is { } item && (target ?? 0) < host.Overlays:
                host.Separate(target ?? 0, item);
                break;
            case MenuEntry.ResetMinMax:
                host.History.Reset(On(host, target).Select(at => host.Settings.Config.Overlays[at]));
                break;
            case MenuEntry.Exit:
                host.Exit();
                break;
            case MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay or MenuEntry.ClickThrough or MenuEntry.Lock
                or MenuEntry.Graph or MenuEntry.Vertical or MenuEntry.Look or MenuEntry.Size or MenuEntry.Bigger or MenuEntry.Smaller:
                Write(host, row, MenuEdits.EditOf(row, On(host, target), target ?? 0, Looks(host.Settings)));
                break;
            case MenuEntry.Separate or MenuEntry.GraphOwn or MenuEntry.SeparateFull or MenuEntry.Overlay or MenuEntry.Update or MenuEntry.SizeOnTaskbar:
            default:
                break;
        }

        host.RequestPaint();
    }

    /// <summary>The looks saved beside the settings, which the menus offer to wear.</summary>
    public static IReadOnlyList<StyleConfig> Looks(Settings settings) => new SavedStyles(ConfigStore.StylesFolder(settings.FilePath)).List();

    private static IReadOnlyList<int> On(IMenuHost host, int? target) =>
        MenuEdits.On(target, Math.Min(host.Settings.Config.Overlays.Count, host.Overlays));

    // Writes only the difference, so what another process wrote meanwhile stays.
    private static void Write(IMenuHost host, MenuRow row, Func<ThermiConfig, ThermiConfig>? edit)
    {
        var before = host.Settings.Config;
        var after = edit is null ? before : host.Settle(before, edit(before)).Config;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            return;
        }

        host.Settings.Edit(change);
        if (Menus.UndoOf(row) is { } undo)
        {
            host.Gestures?.Add(GestureKind.Menu, before, after, undo);
        }
    }
}
