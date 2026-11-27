using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
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

    public Action RequestPaint { get; init; } = () => { };

    /// <summary>Opens the settings window on an overlay, by its place, and an item of it, or none.</summary>
    public Action<int, string?> OpenSettings { get; init; } = (_, _) => { };

    /// <summary>Shows an overlay's menu at a point of the stage, on an item or, null, its edge.</summary>
    public Action<int, double, double, string?> Menu { get; init; } = (_, _, _, _) => { };

    /// <summary>A gesture's or a menu's settings, with the overlays it pushed along a taskbar keeping their places, or refused.</summary>
    public Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; } = (_, after) => new EditResult(after);

    /// <summary>A call to the Shell, whose failure is logged, never thrown: the next paint sends everything again.</summary>
    public void Call(Task sent) => _ = sent.ContinueWith(
        failed => Log.ShellCallFailed(failed.Exception!.GetBaseException()),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted,
        TaskScheduler.Default);
}
