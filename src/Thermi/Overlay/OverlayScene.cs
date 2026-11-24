using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Gestures;
using Thermi.Windows;
using Select = Thermi.Core.Messages.Select;

namespace Thermi.Overlay;

internal sealed record OverlayActions(CultureInfo Culture, TimeProvider Time, Action RequestPaint, Action<int, string?> OpenSettings, Action Exit, Func<Position> Cursor)
{
    /// <summary>A value or an edge clicked or pointed at, which the open window selects or marks.</summary>
    public Action<Select> Select { get; init; } = _ => { };

    /// <summary>The place of another overlay shown under a point of the screen, the first given apart, or null.</summary>
    public Func<int, Position, int?> OverlayAt { get; init; } = (_, _) => null;

    /// <summary>The last desktop gesture, which the overlays' menus offer to undo.</summary>
    public GestureUndo? Gestures { get; init; }

    /// <summary>Shows an overlay's menu, which the host builds with thermi's.</summary>
    public Action<int> Menu { get; init; } = _ => { };

    /// <summary>Every overlay's items' last minute and running statistics, by item, which the tray icons show too.</summary>
    public ItemHistory History { get; init; } = new(Time);
}

internal sealed record OverlayScene(ThermiConfig Config, ConfigProblem? Problem, Snapshot? Snapshot, IReadOnlyList<Screen> Screens, bool LightTaskbar, HighContrastColors? HighContrast, bool WindowOpen)
{
    public ServiceState Service { get; init; } = ServiceState.Running;

    /// <summary>Whether the window has opened since thermi was first started, after which the tooltips stop offering to set it up.</summary>
    public bool OpenedOnce { get; init; } = true;

    /// <summary>Whether apps are in light mode, which the values' tooltips follow as thermi's menus do.</summary>
    public bool LightApps { get; init; } = true;

    /// <summary>Whether Windows' animation effects are on, without which no pill glows.</summary>
    public bool Animate { get; init; }

    /// <summary>The CPU's base clock, read once, and what is busiest now, read only while a CPU or GPU value is critical.</summary>
    public CpuWatch? Cpu { get; init; }
}
