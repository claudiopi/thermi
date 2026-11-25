using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>
/// An entry of thermi's menu or an overlay's; the overlay draws them as Windows' own menus, each with its text of the
/// translations: <see cref="Look"/>'s is the default look's, <see cref="Overlay"/>'s and <see cref="ShowOverlay"/>'s take
/// the overlay's name, <see cref="EditItem"/>'s an item's and <see cref="Separate"/>'s its pill's, and
/// <see cref="Graph"/>'s choices are the graph's own.
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
    EditItem,
    Separate,
    Graph,

    /// <summary>Under the overlay's Graph choices, grayed: items that chose their own keep it.</summary>
    GraphOwn,

    /// <summary>The note beside a grayed "Separate": no other overlay fits.</summary>
    SeparateFull,
}

/// <summary>
/// One entry: <see cref="Overlay"/> names the overlay a submenu, a "Show" or a "Hide" is about, <see cref="Look"/> the saved
/// look it puts on, null for Default, <see cref="Item"/> the item an edit or a separate is about, and <see cref="Graph"/>
/// the graph a choice of the Graph submenu draws; <see cref="Entries"/> is a submenu's.
/// </summary>
public sealed record MenuRow(MenuEntry Entry, bool Checked = false, bool Default = false)
{
    public int? Overlay { get; init; }

    public string? Look { get; init; }

    public string? Item { get; init; }

    /// <summary>One of the Graph submenu's choices.</summary>
    public GraphStyle? Graph { get; init; }

    /// <summary>
    /// True for a row shown grayed, which nothing chooses: one that only says what the menu acts on, or an entry refused
    /// now, with its <see cref="Note"/>.
    /// </summary>
    public bool Heading { get; init; }

    /// <summary>Why an entry is grayed, shown beside it as a hotkey is.</summary>
    public MenuEntry? Note { get; init; }

    /// <summary>True for the first row of a group, set apart by a separator.</summary>
    public bool Starts { get; init; }

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
/// way back for hidden overlays when there is one, else "Settings…", which is an overlay's too, unless it opens on a pill,
/// whose "Edit" opens the window on its item.
/// </summary>
public static class Menus
{
    /// <summary>How long the overlay's menu offers to undo the last desktop gesture.</summary>
    public static readonly TimeSpan GestureUndo = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<MenuEntry, OverlayText> Undos = new()
    {
        [MenuEntry.Separate] = OverlayText.UndoSeparate,
        [MenuEntry.Graph] = OverlayText.UndoGraph,
        [MenuEntry.Vertical] = OverlayText.UndoOrientation,
        [MenuEntry.Lock] = OverlayText.UndoLock,
        [MenuEntry.ClickThrough] = OverlayText.UndoClickThrough,
        [MenuEntry.Look] = OverlayText.UndoLook,
        [MenuEntry.HideThisOverlay] = OverlayText.UndoHide,
        [MenuEntry.ShowOverlay] = OverlayText.UndoShow,
        [MenuEntry.ShowHiddenOverlays] = OverlayText.UndoShow,
    };

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

        rows.Add(new MenuRow(MenuEntry.Settings, Default: back is null) { Starts = true });
        rows.Add(new MenuRow(MenuEntry.TaskManager));
        if (overlays.Count == 1)
        {
            rows.AddRange(Shape(overlays[0], 0));
        }

        // In the overlay's menu's order, so the two read alike.
        rows.Add(new MenuRow(MenuEntry.ClickThrough, overlays.All(overlay => overlay.ClickThrough)) { Hotkey = hotkeys?.ClickThrough });
        rows.Add(new MenuRow(MenuEntry.Lock, overlays.All(overlay => overlay.Locked)));
        if (overlays.Any(ShowsMinOrMax) || hotkeys?.ResetMinMax is not null)
        {
            rows.Add(new MenuRow(MenuEntry.ResetMinMax) { Hotkey = hotkeys?.ResetMinMax });
        }

        if (!state.HideOverlays && overlays.Any(overlay => !overlay.Hidden))
        {
            rows.Add(new MenuRow(MenuEntry.HideOverlays) { Hotkey = hotkeys?.Toggle });
        }

        if (overlays.Count > 1)
        {
            rows.AddRange(overlays.Select((overlay, at) => new MenuRow(MenuEntry.Overlay) { Overlay = at, Starts = at == 0, Entries = Submenu(overlay, at) }));
        }

        if (state.UpdateReady)
        {
            rows.Add(new MenuRow(MenuEntry.Update));
        }

        rows.Add(new MenuRow(MenuEntry.Exit) { Starts = true });
        return rows;
    }

    /// <summary>
    /// An overlay's menu, opened on the pill of <paramref name="item"/> or on its edge. From <paramref name="fromTray"/>
    /// it starts by naming the overlay and offers click-through, which on the overlay itself would let the very click
    /// that turns it back off pass through. An entry that would change nothing in the current state is left out.
    /// </summary>
    public static IReadOnlyList<MenuRow> OfOverlay(ThermiConfig config, int overlay, MenuState state, string? item = null, bool fromTray = false)
    {
        var shown = config.Overlays[overlay];
        var on = item is not null && shown.Items.Any(each => each.Id == item) ? item : null;
        var shows = !shown.Hidden && !state.HideOverlays;
        MenuRow[] edit = on is null ? [] : [new MenuRow(MenuEntry.EditItem, Default: true) { Item = on }];
        MenuRow[] undo = state.SinceGesture < GestureUndo ? [new MenuRow(MenuEntry.UndoGesture)] : [];
        MenuRow[] reset = ShowsMinOrMax(shown) ? [new MenuRow(MenuEntry.ResetMinMax) { Hotkey = config.Hotkeys?.ResetMinMax }] : [];
        MenuRow[] copy = shows ? [new MenuRow(MenuEntry.CopyImage) { Overlay = overlay }] : [];
        return WithSeparators(
        [
            fromTray ? [new MenuRow(MenuEntry.Overlay) { Overlay = overlay, Heading = true }] : [],
            [.. Problems(state, startSensors: false), .. undo],
            [.. edit, new MenuRow(MenuEntry.Settings, Default: on is null)],
            ChangeRows(config, overlay, on is not null && shows ? on : null, state, fromTray),
            [.. copy, new MenuRow(MenuEntry.TaskManager), .. reset],
            HideRows(config, overlay, state),
            [new MenuRow(MenuEntry.Exit)],
        ]);
    }

    /// <summary>
    /// What "Edit" and "Separate" name: an item as the window's list says it; for "Separate" of a pill with several items,
    /// its label, as "CPU pill".
    /// </summary>
    public static string NameOf(ThermiConfig config, MenuRow row, Snapshot? known, Texts texts, IFormatProvider numbers)
    {
        if (row.Item is not { } id || ConfigEdits.Find(config, id) is not var (overlay, index))
        {
            return "";
        }

        var shown = config.Overlays[overlay];
        var (first, last) = row.Entry == MenuEntry.Separate ? ItemEdits.PillOf(shown, index) : (index, index);
        return first < last && OverlayLayout.RunLabels(shown.Items, texts)[first] is { } label
            ? texts.Format(numbers, UiText.PillName, label)
            : shown.Items[index].Name ?? Names.OfUnnamed(shown.Items[index], known, texts);
    }

    /// <summary>The overlay's menu entry that takes back an edit chosen from a menu, null for what is no edit of the settings.</summary>
    public static OverlayText? UndoOf(MenuRow row) => Undos.TryGetValue(row.Entry, out var undo) ? undo : null;

    private static List<MenuRow> WithSeparators(IEnumerable<IReadOnlyList<MenuRow>> groups)
    {
        var rows = new List<MenuRow>();
        foreach (var group in groups.Where(group => group.Count > 0))
        {
            var first = rows.Count == 0;
            rows.Add(first ? group[0] : group[0] with { Starts = true });
            rows.AddRange(group.Skip(1));
        }

        return rows;
    }

    private static List<MenuRow> ChangeRows(ThermiConfig config, int overlay, string? pill, MenuState state, bool fromTray)
    {
        var shown = config.Overlays[overlay];
        var rows = new List<MenuRow>();
        // A locked overlay holds its pills as it holds itself, as a hold to lift one shows; at the cap a hold lifts none.
        if (pill is not null && !shown.Locked && DesktopGestures.IsPart(config, overlay, pill))
        {
            var full = !DesktopGestures.CanTakeOff(config, overlay, pill);
            rows.Add(new MenuRow(MenuEntry.Separate) { Item = pill, Heading = full, Note = full ? MenuEntry.SeparateFull : null });
        }

        if (GraphOf(shown) is { } graph)
        {
            rows.Add(graph);
        }

        rows.AddRange(LooksOf(shown, state));
        rows.AddRange(VerticalOf(shown, null));
        if (fromTray)
        {
            rows.Add(new MenuRow(MenuEntry.ClickThrough, shown.ClickThrough) { Overlay = overlay });
        }

        rows.Add(new MenuRow(MenuEntry.Lock, shown.Locked));
        return rows;
    }

    // Only items that a line judges and that chose no graph of their own draw the overlay's Graph.
    private static MenuRow? GraphOf(OverlayConfig overlay)
    {
        var judged = overlay.Items.Where(Threshold.Judges).ToList();
        if (!judged.Exists(item => item.Graph is null))
        {
            return null;
        }

        MenuRow[] own = judged.Exists(item => item.Graph is not null) ? [new MenuRow(MenuEntry.GraphOwn) { Heading = true, Starts = true }] : [];
        return new MenuRow(MenuEntry.Graph)
        {
            Entries = [.. Enum.GetValues<GraphStyle>().Select(style => new MenuRow(MenuEntry.Graph, style == overlay.Graph) { Graph = style }), .. own],
        };
    }

    // "Hide overlays" hides nothing while every overlay is hidden on its own.
    private static List<MenuRow> HideRows(ThermiConfig config, int overlay, MenuState state)
    {
        var rows = new List<MenuRow>();
        if (config.Overlays[overlay].Hidden)
        {
            rows.Add(new MenuRow(MenuEntry.ShowOverlay) { Overlay = overlay });
        }
        else if (config.Overlays.Count > 1)
        {
            rows.Add(new MenuRow(MenuEntry.HideThisOverlay) { Overlay = overlay });
        }

        if (state.HideOverlays)
        {
            rows.Add(new MenuRow(MenuEntry.ShowOverlays) { Hotkey = config.Hotkeys?.Toggle });
        }
        else if (config.Overlays.Any(each => !each.Hidden))
        {
            rows.Add(new MenuRow(MenuEntry.HideOverlays) { Hotkey = config.Hotkeys?.Toggle });
        }

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
        .. VerticalOf(overlay, at),
    ];

    // Offered while it would stack pills, or while it is on, so it can always be turned off.
    private static MenuRow[] VerticalOf(OverlayConfig overlay, int? at)
    {
        var vertical = overlay.Style.Orientation == Orientation.Vertical;
        return vertical || OverlayLayout.Stacks(overlay) ? [new MenuRow(MenuEntry.Vertical, vertical) { Overlay = at }] : [];
    }

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
        .. Shape(overlay, at),
        new MenuRow(MenuEntry.ClickThrough, overlay.ClickThrough) { Overlay = at },
        new MenuRow(MenuEntry.Lock, overlay.Locked) { Overlay = at },
        overlay.Hidden ? new MenuRow(MenuEntry.ShowOverlay) { Overlay = at } : new MenuRow(MenuEntry.HideThisOverlay) { Overlay = at },
    ];
}
