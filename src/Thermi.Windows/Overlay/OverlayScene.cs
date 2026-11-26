using System.Globalization;
using Thermi.App.Gestures;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
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
}
