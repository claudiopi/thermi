using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Overlay;

/// <summary>What the overlays, their menu and the host share, on the GLib main loop's thread.</summary>
internal sealed record OverlayParts(Settings Settings, ShellLink Link, IOverlaySurfaces Surfaces, TimeProvider Time, CultureInfo Culture, ILogger Log)
{
    /// <summary>When thermi started, from which an overlay on a taskbar that is not up yet waits for it.</summary>
    public long Started { get; } = Time.GetTimestamp();

    public ItemHistory History { get; } = new(Time);

    /// <summary>The last desktop gesture or menu edit, which the overlay's menu offers to take back.</summary>
    public GestureUndo Gestures { get; } = new(Time, _ => { });

    public OverlayVisibility Visibility { get; } = new();

    /// <summary>The overlay a drag is over now, and where a drop would join it, which that overlay marks.</summary>
    public DropPreview Preview { get; } = new();

    /// <summary>The person's drag threshold and double-click time, which a press on an overlay keeps to.</summary>
    public PointerSettings Pointer { get; } = new();

    /// <summary>Where the pointer was last told, in thermi's pixels, and its keys; none until a surface tells it.</summary>
    public (Position At, uint Keys)? Cursor { get; set; }

    /// <summary>The accent a drop's marks draw in.</summary>
    public Rgb Accent { get; init; } = DesktopLooks.Accent(null);

    /// <summary>Whether thermi runs under <c>--trace</c>, which the settings window it starts then runs under too.</summary>
    public bool Traced { get; init; }

    public Action RequestPaint { get; init; } = () => { };

    /// <summary>Runs on the main loop's thread once it is free, from any thread.</summary>
    public Action<Action> Post { get; init; } = action => action();

    /// <summary>Opens the settings window on an overlay, by its place, and an item of it, or none.</summary>
    public Action<int, string?> OpenSettings { get; init; } = (_, _) => { };

    /// <summary>Shows an overlay's menu at a point of the stage, on an item or, null, its edge.</summary>
    public Action<int, double, double, string?> Menu { get; init; } = (_, _, _, _) => { };

    /// <summary>A gesture's or a menu's settings, with the overlays it pushed along a taskbar keeping their places, or refused.</summary>
    public Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; } = (_, after) => new EditResult(after);

    /// <summary>A drag or a hold ended, true when it wrote the settings, whose save then says it is done.</summary>
    public Action<bool> Dropped { get; init; } = _ => { };

    /// <summary>thermi is opening the settings window, which ends its first start.</summary>
    public Action WindowOpening { get; init; } = () => { };

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
