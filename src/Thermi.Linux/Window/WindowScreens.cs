using Microsoft.Extensions.Logging;
using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;
using Thermi.Linux.X11;

namespace Thermi.Linux.Window;

/// <summary>
/// The monitors the window's map draws, and its places are worked out on, as the overlay has them, so a spot picked on the
/// map is the overlay's own and the window says where it shows: on GNOME the Shell's, through thermi's extension where the
/// overlays show through it, with the top bar as their taskbar and the names a snap keeps; elsewhere the running overlay's,
/// with the panels' room it measures, which a Wayland compositor tells only its layer surfaces; and before either answers,
/// or with no overlay running, GDK's, read as the overlay's surfaces read them, with the names, the primary one and, on X11,
/// the panels' room they give. Used on GTK's loop alone.
/// </summary>
internal sealed class WindowScreens
{
    private static readonly IReadOnlyDictionary<(int X, int Y), string> Unnamed = new Dictionary<(int X, int Y), string>();

    private readonly Gio.DBusConnection? _bus;
    private readonly ShellLink? _link;
    private readonly Action<Action> _post;
    private readonly Action _changed;
    private readonly ILogger _log;
    private ShellScreens? _shell;
    private IReadOnlyList<Screen>? _overlay;

    // Whether the overlays show through the extension, whose monitors its signal sends too.
    private bool _ours;

    public WindowScreens(Gio.DBusConnection? bus, ShellLink? link, Action<Action> post, Action changed, ILogger log)
    {
        _bus = bus;
        _post = post;
        _changed = changed;
        _log = log;
        if (Gdk.Display.GetDefault()?.GetMonitors() is { } monitors)
        {
            monitors.OnItemsChanged += (_, _) =>
            {
                changed();
                AskOverlay();
            };
        }

        AskOverlay();
        if (link is null)
        {
            return;
        }

        _link = link;
        _link.MonitorsChanged = monitors =>
        {
            if (_ours)
            {
                Taken(monitors, changed);
            }
        };
        _ = ReadAsync(changed);
    }

    public IReadOnlyList<Screen> Screens => _shell is { Monitors.Count: > 0 } shell ? shell.Screens : Current(new ShellScreens(OfGdk(), Unnamed).Screens);

    // The overlay's answer while its monitors are GDK's now: one it gave before a monitor came, went or moved is GDK's
    // until it answers again.
    private IReadOnlyList<Screen> Current(IReadOnlyList<Screen> gdk) => _overlay is { } overlay && Bounds(overlay).SequenceEqual(Bounds(gdk)) ? overlay : gdk;

    private static IEnumerable<PixelRect> Bounds(IReadOnlyList<Screen> screens) =>
        screens.Select(screen => screen.Bounds).OrderBy(bounds => bounds.Left).ThenBy(bounds => bounds.Top);

    /// <summary>
    /// Asks the running overlay its monitors again, as it started, ended or the monitors changed, and draws the map again
    /// once it answered; one that does not run, or of another shape, leaves GDK's.
    /// </summary>
    public void AskOverlay()
    {
        if (_bus is not null)
        {
            _ = AskOverlayAsync(_bus);
        }
    }

    private async Task AskOverlayAsync(Gio.DBusConnection bus)
    {
        OverlayState? state = null;
        try
        {
            using var answer = await bus.CallAsync(ShellLink.Name, OverlayObject.Path, OverlayObject.Name, OverlayObject.State, null).ConfigureAwait(false);
            state = OverlayState.Read(answer);
        }
        catch (GLib.GException)
        {
            // No overlay runs, or it ended as it was asked.
        }

        _post(() =>
        {
            _overlay = state is { Screens.Count: > 0 } ? state.Screens : null;
            _changed();
        });
    }

    private IReadOnlyList<ShellMonitor> OfGdk() => Gdk.Display.GetDefault() is { } display && !DesktopSession.Wayland(display)
        ? X11Surfaces.MonitorsOf(_log, display, covered: null)
        : GdkMonitors.Read(_log, GdkMonitors.All(), _ => default, primary: null);

    // Without the extension the Shell has no such object, and one the overlays do not show through is asked no monitors:
    // the window keeps the overlay's or GDK's.
    private async Task ReadAsync(Action changed)
    {
        try
        {
            var monitors = await _link!.MonitorsAsync().ConfigureAwait(false);
            if (monitors.Count > 0)
            {
                _post(() =>
                {
                    _ours = true;
                    Taken(monitors, changed);
                });
            }
        }
        catch (GLib.GException)
        {
            // No extension answers on this desktop.
        }
    }

    private void Taken(IReadOnlyList<ShellMonitor> monitors, Action changed)
    {
        _shell = new ShellScreens(monitors, ShellScreens.Names(_bus!));
        changed();
    }
}
