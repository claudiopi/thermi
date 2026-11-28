using Microsoft.Extensions.Logging;
using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.X11;

namespace Thermi.Linux.Window;

/// <summary>
/// The monitors the window's map draws, as the overlay has them, so a spot picked on the map is the overlay's own: on GNOME
/// the Shell's, through thermi's extension where the overlays show through it, with the top bar as their taskbar and the
/// names a snap keeps; elsewhere, with an extension they do not show through, or before the Shell answers, GDK's, read as
/// the overlay's surfaces read them, with the names, the primary one and, on X11, the panels' room they give. A Wayland
/// compositor tells the panels' room only to the layer surfaces the overlay measures it with, so there the map has no bar.
/// Used on GTK's loop alone.
/// </summary>
internal sealed class WindowScreens
{
    private static readonly IReadOnlyDictionary<(int X, int Y), string> Unnamed = new Dictionary<(int X, int Y), string>();

    private readonly Gio.DBusConnection? _bus;
    private readonly ShellLink? _link;
    private readonly Action<Action> _post;
    private readonly ILogger _log;
    private ShellScreens? _shell;

    // Whether the overlays show through the extension, whose monitors its signal sends too.
    private bool _ours;

    public WindowScreens(Gio.DBusConnection? bus, ShellLink? link, Action<Action> post, Action changed, ILogger log)
    {
        _bus = bus;
        _post = post;
        _log = log;
        if (Gdk.Display.GetDefault()?.GetMonitors() is { } monitors)
        {
            monitors.OnItemsChanged += (_, _) => changed();
        }

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

    public IReadOnlyList<Screen> Screens => _shell is { Monitors.Count: > 0 } shell ? shell.Screens : new ShellScreens(OfGdk(), Unnamed).Screens;

    private IReadOnlyList<ShellMonitor> OfGdk() => Gdk.Display.GetDefault() is { } display && !DesktopSession.Wayland(display)
        ? X11Surfaces.MonitorsOf(_log, display, covered: null)
        : GdkMonitors.Read(_log, GdkMonitors.All(), _ => default, primary: null);

    // Without the extension the Shell has no such object, and one the overlays do not show through is asked no monitors:
    // the window keeps GDK's.
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
