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
internal sealed record OverlayParts(Settings Settings, ShellLink Link, TimeProvider Time, CultureInfo Culture, ILogger Log)
{
    /// <summary>When thermi started, from which an overlay on a taskbar that is not up yet waits for it.</summary>
    public long Started { get; } = Time.GetTimestamp();

    public ItemHistory History { get; } = new(Time);

    /// <summary>The last desktop gesture or menu edit, which the overlay's menu offers to take back.</summary>
    public GestureUndo Gestures { get; } = new(Time, _ => { });

    public OverlayVisibility Visibility { get; } = new();

    /// <summary>The person's drag threshold and double-click time, which a press on an overlay keeps to.</summary>
    public PointerSettings Pointer { get; } = new();

    /// <summary>Whether thermi runs under <c>--trace</c>, which the settings window it starts then runs under too.</summary>
    public bool Traced { get; init; }

    public Action RequestPaint { get; init; } = () => { };

    /// <summary>Opens the settings window on an overlay, by its place, and an item of it, or none, on a click at a time of the Shell's.</summary>
    public Action<int, string?, uint> OpenSettings { get; init; } = (_, _, _) => { };

    /// <summary>
    /// The desktop's token for a program the overlay starts on a click at a time of the Shell's, or now for 0, or empty: the
    /// window it shows with it comes in front, as the desktop lets one its own launcher started.
    /// </summary>
    public Func<uint, Task<string>> Activation { get; init; } = _ => Task.FromResult("");

    /// <summary>Shows an overlay's menu at a point of the stage, on an item or, null, its edge.</summary>
    public Action<int, double, double, string?> Menu { get; init; } = (_, _, _, _) => { };

    /// <summary>A gesture's or a menu's settings, with the overlays it pushed along a taskbar keeping their places, or refused.</summary>
    public Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; } = (_, after) => new EditResult(after);

    /// <summary>A drag or a hold ended, true when it wrote the settings, whose save then says it is done.</summary>
    public Action<bool> Dropped { get; init; } = _ => { };

    /// <summary>
    /// A call to the Shell, done once the Shell answered; its failure is logged, never thrown, as the next paint sends
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
