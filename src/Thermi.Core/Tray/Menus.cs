using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>
/// An entry of thermi's menu or an overlay's; the overlay draws them as Windows' own menus, each with its text of the
/// translations: <see cref="Look"/>'s is the default look's, <see cref="Overlay"/>'s and <see cref="ShowOverlay"/>'s take
/// the overlay's name.
/// </summary>
[Translated]
public enum MenuEntry
{
    SettingsBroken,
    StartSensors,
    ShowOverlays,
    ShowOverlay,
    ShowHiddenOverlays,
    UndoGesture,
    Settings,
    TaskManager,
    CopyImage,
    Look,
    Vertical,
    HideOverlays,
    HideThisOverlay,
    ClickThrough,
    Lock,
    ResetMinMax,
    Overlay,
    Update,
    Exit,
}

/// <summary>
/// One entry: <see cref="Overlay"/> names the overlay a submenu, a "Show" or a "Hide" is about, <see cref="Look"/> the saved
/// look it puts on, null for Default; <see cref="Entries"/> is a submenu's.
/// </summary>
public sealed record MenuRow(MenuEntry Entry, bool Checked = false, bool Default = false)
{
    public int? Overlay { get; init; }

    public string? Look { get; init; }

    /// <summary>True for a row that only says what the menu acts on, shown grayed, which nothing chooses.</summary>
    public bool Heading { get; init; }

    /// <summary>The hotkey shown beside the entry, as the file writes it, or null.</summary>
    public string? Hotkey { get; init; }

    public IReadOnlyList<MenuRow> Entries { get; init; } = [];
}

/// <summary>
/// What the menus show besides the settings: the hide flag, the file's and the service's state, the last desktop gesture's age,
/// the saved looks and an update.
/// </summary>
public sealed record MenuState(
    bool HideOverlays,
    bool FileBroken,
    ServiceState Service,
    TimeSpan? SinceGesture,
    IReadOnlyList<string> SavedLooks,
    bool UpdateReady);

/// <summary>
/// thermi's menu and each overlay's, as data in their order, so both are built in one place. thermi's default entry is the
/// way back for hidden overlays when there is one, else "Settings…", which is always an overlay's.
/// </summary>
public static class Menus
{
    /// <summary>How long the overlay's menu offers to undo the last desktop gesture.</summary>
    public static readonly TimeSpan GestureUndo = TimeSpan.FromSeconds(30);

    /// <summary>Whether an overlay has an item that shows a minimum or a maximum, which "Reset min and max" starts again.</summary>
    public static bool ShowsMinOrMax(OverlayConfig overlay) => overlay.Items.Any(item => item.Stat is ItemStat.Min or ItemStat.Max);

    public static IReadOnlyList<MenuRow> Thermis(ThermiConfig config, MenuState state)
    {
        var (overlays, hotkeys) = (config.Overlays, config.Hotkeys);
        var back = WayBack(config, state);
        var rows = Problems(state, startSensors: true);
        if (back is not null)
        {
            rows.Add(back);
        }

        rows.Add(new MenuRow(MenuEntry.Settings, Default: back is null));
        rows.Add(new MenuRow(MenuEntry.TaskManager));
        if (overlays.Count == 1)
        {
            rows.AddRange(Shape(overlays[0], 0));
        }

        if (!state.HideOverlays)
        {
            rows.Add(new MenuRow(MenuEntry.HideOverlays) { Hotkey = hotkeys?.Toggle });
        }

        rows.Add(new MenuRow(MenuEntry.ClickThrough, overlays.All(overlay => overlay.ClickThrough)) { Hotkey = hotkeys?.ClickThrough });
        rows.Add(new MenuRow(MenuEntry.Lock, overlays.All(overlay => overlay.Locked)));
        if (overlays.Any(ShowsMinOrMax) || hotkeys?.ResetMinMax is not null)
        {
            rows.Add(new MenuRow(MenuEntry.ResetMinMax) { Hotkey = hotkeys?.ResetMinMax });
        }

        if (overlays.Count > 1)
        {
            rows.AddRange(overlays.Select((overlay, at) => new MenuRow(MenuEntry.Overlay) { Overlay = at, Entries = Submenu(overlay, at) }));
        }

        if (state.UpdateReady)
        {
            rows.Add(new MenuRow(MenuEntry.Update));
        }

        rows.Add(new MenuRow(MenuEntry.Exit));
        return rows;
    }

    /// <summary>
    /// An overlay's menu, from the overlay itself or, <paramref name="fromTray"/>, from one of its items' tray icons: there,
    /// away from the overlay, it starts by naming the overlay and offers click-through, which on the overlay itself would
    /// let the very click that turns it back off pass through.
    /// </summary>
    public static IReadOnlyList<MenuRow> OfOverlay(ThermiConfig config, int overlay, MenuState state, bool fromTray = false)
    {
        var shown = config.Overlays[overlay];
        var rows = Problems(state, startSensors: false);
        if (fromTray)
        {
            rows.Insert(0, new MenuRow(MenuEntry.Overlay) { Overlay = overlay, Heading = true });
        }

        if (state.SinceGesture < GestureUndo)
        {
            rows.Add(new MenuRow(MenuEntry.UndoGesture));
        }

        rows.Add(new MenuRow(MenuEntry.Settings, Default: true));
        rows.Add(new MenuRow(MenuEntry.TaskManager));
        rows.Add(new MenuRow(MenuEntry.CopyImage) { Overlay = overlay });
        rows.AddRange(LooksOf(shown, state));
        rows.Add(new MenuRow(MenuEntry.Vertical, shown.Style.Orientation == Orientation.Vertical));
        if (fromTray)
        {
            rows.Add(new MenuRow(MenuEntry.ClickThrough, shown.ClickThrough) { Overlay = overlay });
        }

        rows.Add(new MenuRow(MenuEntry.Lock, shown.Locked));
        if (ShowsMinOrMax(shown))
        {
            rows.Add(new MenuRow(MenuEntry.ResetMinMax) { Hotkey = config.Hotkeys?.ResetMinMax });
        }

        rows.Add(config.Overlays.Count == 1
            ? new MenuRow(MenuEntry.HideOverlays) { Hotkey = config.Hotkeys?.Toggle }
            : new MenuRow(MenuEntry.HideThisOverlay) { Overlay = overlay });
        rows.Add(new MenuRow(MenuEntry.Exit));
        return rows;
    }

    private static IEnumerable<MenuRow> LooksOf(OverlayConfig shown, MenuState state)
    {
        if (state.SavedLooks.Count == 0)
        {
            return [];
        }

        var worn = state.SavedLooks.FirstOrDefault(look => string.Equals(look, shown.Style.Name, StringComparison.OrdinalIgnoreCase));
        return
        [
            new MenuRow(MenuEntry.Look, worn is null && shown.Style.WornOn(new StyleConfig()) == new StyleConfig()),
            .. state.SavedLooks.Select(look => new MenuRow(MenuEntry.Look, look == worn) { Look = look }),
        ];
    }

    // What an overlay's own menu does to its picture and shape, which thermi's menu offers too, for it or in its submenu.
    private static MenuRow[] Shape(OverlayConfig overlay, int at) =>
    [
        new MenuRow(MenuEntry.CopyImage) { Overlay = at },
        new MenuRow(MenuEntry.Vertical, overlay.Style.Orientation == Orientation.Vertical) { Overlay = at },
    ];

    private static List<MenuRow> Problems(MenuState state, bool startSensors)
    {
        var rows = new List<MenuRow>();
        if (state.FileBroken)
        {
            rows.Add(new MenuRow(MenuEntry.SettingsBroken));
        }

        if (startSensors && state.Service == ServiceState.Stopped)
        {
            rows.Add(new MenuRow(MenuEntry.StartSensors));
        }

        return rows;
    }

    // "Show overlays" clears the flag; else the one overlay hidden on its own, or all of them.
    private static MenuRow? WayBack(ThermiConfig config, MenuState state)
    {
        var hidden = Enumerable.Range(0, config.Overlays.Count).Where(at => config.Overlays[at].Hidden).ToList();
        return state.HideOverlays ? new MenuRow(MenuEntry.ShowOverlays, Default: true)
            : hidden.Count == 1 ? new MenuRow(MenuEntry.ShowOverlay, Default: true) { Overlay = hidden[0] }
            : hidden.Count > 1 ? new MenuRow(MenuEntry.ShowHiddenOverlays, Default: true)
            : null;
    }

    private static List<MenuRow> Submenu(OverlayConfig overlay, int at) =>
    [
        overlay.Hidden ? new MenuRow(MenuEntry.ShowOverlay) { Overlay = at } : new MenuRow(MenuEntry.HideThisOverlay) { Overlay = at },
        new MenuRow(MenuEntry.ClickThrough, overlay.ClickThrough) { Overlay = at },
        new MenuRow(MenuEntry.Lock, overlay.Locked) { Overlay = at },
        .. Shape(overlay, at),
    ];
}
