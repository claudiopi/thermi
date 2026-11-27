using System.Globalization;
using Thermi.App.Gestures;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Win32;
using Select = Thermi.Core.Messages.Select;

namespace Thermi.Windows.Overlay;

internal sealed record OverlayActions(
    CultureInfo Culture,
    TimeProvider Time,
    Action RequestPaint,
    Action<int, string?> OpenSettings,
    Action Exit,
    Func<Position> Cursor)
{
    /// <summary>Opens the window on an overlay's Size row, by its place.</summary>
    public Action<int> OpenSize { get; init; } = _ => { };

    /// <summary>A value or an edge clicked or pointed at, which the open window selects or marks.</summary>
    public Action<Select> Select { get; init; } = _ => { };

    /// <summary>Each overlay's box on the screen, by its place, null for one not shown.</summary>
    public Func<IReadOnlyList<PixelRect?>> Shown { get; init; } = () => [];

    /// <summary>
    /// Where a drop at a point of the screen joins an overlay: before the pill of its item drawn first past it, null past its
    /// last; or, for the item named, carried alone over its own pill there, before that pill's item drawn first past it.
    /// </summary>
    public Func<int, Position, string?, DropTarget> JoinAt { get; init; } = (onto, _, _) => new DropTarget(onto, null);

    /// <summary>
    /// A gesture's or a menu's settings, with each overlay it pushed along a taskbar keeping the place it was pushed to,
    /// written and undone with it; or why it is refused, as a move onto a taskbar without room.
    /// </summary>
    public Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; } = (_, after) => new EditResult(after);

    /// <summary>The overlay a drag is over now, and where a drop would join it, which that overlay marks.</summary>
    public DropPreview Preview { get; } = new();

    /// <summary>When the last drop let go, after which the overlay left under the pointer shows no tooltip for a while.</summary>
    public DropQuiet Quiet { get; } = new(Time);

    /// <summary>The last desktop gesture, which the overlays' menus offer to undo.</summary>
    public GestureUndo? Gestures { get; init; }

    /// <summary>Shows an overlay's menu, on the item under the pointer or null on its edge; the host builds it with thermi's.</summary>
    public Action<int, string?> Menu { get; init; } = (_, _) => { };

    /// <summary>Every overlay's items' last minute and running statistics, by item, which the tray icons show too.</summary>
    public ItemHistory History { get; init; } = new(Time);
}

internal sealed record OverlayScene(
    ThermiConfig Config,
    ConfigProblem? Problem,
    Snapshot? Snapshot,
    IReadOnlyList<Screen> Screens,
    bool LightTaskbar,
    HighContrastColors? HighContrast,
    bool WindowOpen)
{
    public ServiceState Service { get; init; } = ServiceState.Running;

    /// <summary>Whether the window has opened since thermi was first started, after which the tooltips stop offering to set it up.</summary>
    public bool OpenedOnce { get; init; } = true;

    /// <summary>Whether apps are in light mode, which the values' tooltips follow as thermi's menus do.</summary>
    public bool LightApps { get; init; } = true;

    /// <summary>Windows' accent color, which high contrast's highlight stands in for.</summary>
    public Rgb WindowsAccent { get; init; }

    /// <summary>What a drag and the open window mark the overlays in.</summary>
    public Rgb Accent => HighContrast?.Highlight ?? WindowsAccent;

    /// <summary>Whether Windows' animation effects are on, without which no pill glows.</summary>
    public bool Animate { get; init; }

    /// <summary>The CPU's base clock, read once, and what is busiest now, read only while a CPU or GPU value is critical.</summary>
    public CpuWatch? Cpu { get; init; }

    /// <summary>The items an overlay shows, and after them the settings' error while the file is broken.</summary>
    public IReadOnlyList<OverlayItem> WithProblem(Texts texts, IReadOnlyList<OverlayItem> items)
    {
        if (Problem is null)
        {
            return items;
        }

        var error = texts.Of(OverlayText.ConfigError);
        return [.. items, new OverlayItem(texts.Of(OverlayText.ConfigLabel), error, Level.Critical, error)];
    }

    /// <summary>
    /// The settings after an edit or a gesture, from the overlays' own process, this scene showing <paramref name="after"/>:
    /// the others as their windows, <paramref name="shown"/>, box them now.
    /// </summary>
    public EditResult Settled(ThermiConfig before, ThermiConfig after, CultureInfo culture, IReadOnlyList<PixelRect?> shown)
    {
        var input = new TrayInput(Snapshot, Service, culture, Texts.For(after.Language, CultureInfo.CurrentUICulture), LightTaskbar, "");
        using var painter = new Painting.Painter();
        return App.Window.OverlayPlaces.Settled(before, after, input, Screens, shown, painter);
    }
}
