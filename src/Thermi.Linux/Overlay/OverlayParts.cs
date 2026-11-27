using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.Link;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;
using Thermi.Linux.Overlay.Link;

namespace Thermi.Linux.Overlay;

/// <summary>What the overlays, their menu and the host share, on the GLib main loop's thread.</summary>
internal sealed record OverlayParts(Settings Settings, ShellLink Link, IOverlaySurfaces Surfaces, TimeProvider Time, CultureInfo Culture, ILogger Log)
{
    /// <summary>When thermi started, from which an overlay on a taskbar that is not up yet waits for it.</summary>
    public long Started { get; } = Time.GetTimestamp();

    public ItemHistory History { get; } = new(Time);

    /// <summary>What carries the overlay's end of its link with the settings window.</summary>
    public required WindowChannel Channel { get; init; }

    /// <summary>The overlay's end of its link with the settings window: whether it is open, what it edits, what it is told.</summary>
    public OverlayEnd Window => field ??= new OverlayEnd(Channel, Settings, History, () => RequestPaint());

    /// <summary>The last desktop gesture or menu edit, which the overlay's menu offers to take back, and an open window takes as a step.</summary>
    public GestureUndo Gestures => field ??= new GestureUndo(Time, Window.Gestured);

    public OverlayVisibility Visibility { get; } = new();

    /// <summary>The overlay a drag is over now, and where a drop would join it, which that overlay marks.</summary>
    public DropPreview Preview { get; } = new();

    /// <summary>The quiet after the last drop, which holds every overlay's tooltips until the pointer leaves it.</summary>
    public DropQuiet Quiet { get; } = new(Time);

    /// <summary>The person's drag threshold, double-click time and whether things animate, which the overlays keep to.</summary>
    public PointerSettings Pointer { get; } = new();

    /// <summary>Where the pointer was last told, in thermi's pixels, and its keys; none until a surface tells it.</summary>
    public (Position At, uint Keys)? Cursor { get; set; }

    /// <summary>The desktop's look now, as its settings portal says it, which every paint reads.</summary>
    public DesktopLook Look { get; set; } = new(null, DesktopLooks.Accent(null), Dark: true);

    /// <summary>Whether the overlays' menus mark access keys, as GTK's do, and the Shell's, which has none, do not.</summary>
    public bool MarksAccessKeys { get; init; }

    public Action RequestPaint { get; init; } = () => { };

    /// <summary>Runs on the main loop's thread once it is free, from any thread.</summary>
    public Action<Action> Post { get; init; } = action => action();

    /// <summary>The monitors as the surfaces last told them.</summary>
    public Func<ShellScreens> Screens { get; init; } = () => new ShellScreens([], new Dictionary<(int X, int Y), string>());

    /// <summary>Opens the settings window on an overlay, by its place, and an item of it, or none, on a click at a time of the desktop's.</summary>
    public Action<int, string?, uint> OpenSettings { get; init; } = (_, _, _) => { };

    /// <summary>Shows an overlay's menu beside it, the pointer at a point of the screen, on an item or, null, its edge.</summary>
    public Action<int, Position, string?> Menu { get; init; } = (_, _, _) => { };

    /// <summary>A gesture's or a menu's settings, with the overlays it pushed along a taskbar keeping their places, or refused.</summary>
    public Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; } = (_, after) => new EditResult(after);

    /// <summary>A drag or a hold ended, true when it wrote the settings, whose save then says it is done.</summary>
    public Action<bool> Dropped { get; init; } = _ => { };

    /// <summary>Each overlay's box on the screen, by its place, null for one not shown.</summary>
    public Func<IReadOnlyList<PixelRect?>> Shown { get; init; } = () => [];

    /// <summary>Where a drop at a point of the screen joins an overlay, as its painted places say.</summary>
    public Func<int, Position, string?, DropTarget> JoinAt { get; init; } = (onto, _, _) => new DropTarget(onto, null);

    /// <summary>Writes an event of --trace's once what made it shows.</summary>
    public Action<string, Task> Trace { get; init; } = (_, _) => { };

    /// <summary>
    /// A call to the surfaces, done once they answered; its failure is logged, never thrown, as the next paint sends
    /// everything again.
    /// </summary>
    public Task Call(Task sent) => sent.ContinueWith(
        done =>
        {
            if (done.Exception is { } failed)
            {
                Log.ShellCallFailed(failed.GetBaseException());
            }
        },
        CancellationToken.None,
        TaskContinuationOptions.None,
        TaskScheduler.Default);
}

/// <summary>The desktop's look as the overlays draw it: high contrast's colors or none, the accent, and whether its apps are dark.</summary>
internal sealed record DesktopLook(HighContrastColors? HighContrast, Rgb Accent, bool Dark);
