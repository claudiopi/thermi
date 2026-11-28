using Thermi.Core.Placement;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Window;

/// <summary>
/// The monitors the window's map draws, as the overlay has them: on GNOME the Shell's, through thermi's extension of this
/// protocol, with the top bar as their taskbar and the names a snap keeps, so a spot picked on the map is the overlay's
/// own; elsewhere, with an extension of another version, or before the Shell answers, GDK's, with no bar. Used on GTK's
/// loop alone.
/// </summary>
internal sealed class WindowScreens
{
    private readonly Gio.DBusConnection? _bus;
    private readonly ShellLink? _link;
    private readonly Action<Action> _post;
    private ShellScreens? _shell;

    // Whether the extension is of this protocol, whose monitors its signal sends too.
    private bool _ours;

    public WindowScreens(Gio.DBusConnection? bus, ShellLink? link, Action<Action> post, Action changed)
    {
        _bus = bus;
        _post = post;
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

    public IReadOnlyList<Screen> Screens => _shell is { Monitors.Count: > 0 } shell ? shell.Screens : OfGdk();

    private static List<Screen> OfGdk()
    {
        var screens = new List<Screen>();
        if (Gdk.Display.GetDefault()?.GetMonitors() is not { } monitors)
        {
            return screens;
        }

        for (uint at = 0; at < monitors.GetNItems(); at++)
        {
            if (monitors.GetObject(at) is Gdk.Monitor monitor)
            {
                monitor.GetGeometry(out var area);
                using var box = area;
                var scale = monitor.GetScale();
                var bounds = new PixelRect(
                    (int)(box.X * scale), (int)(box.Y * scale), (int)((box.X + box.Width) * scale), (int)((box.Y + box.Height) * scale));
                var name = monitor.GetConnector() ?? at.ToString(System.Globalization.CultureInfo.InvariantCulture);
                screens.Add(new Screen(name, at == 0, bounds, bounds, scale));
            }
        }

        return screens;
    }

    // Without the extension the Shell has no such object, and with one of another version none of this protocol's
    // monitors: the window keeps GDK's.
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
