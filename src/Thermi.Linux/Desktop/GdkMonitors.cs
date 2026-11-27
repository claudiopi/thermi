using Thermi.Core.Placement;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// The monitors as GTK finds them on a desktop other than GNOME, in the stage's logical pixels: each named as GNOME's
/// display config names it, by its EDID's maker, product and serial (<see cref="MonitorIds.OfGnome(ReadOnlySpan{byte}, string)"/>),
/// so a snap follows its monitor as the monitors move and as the desktop changes. GDK tells no serial, on Wayland or X11,
/// so the EDID is the kernel's, by the monitor's connector; a monitor without one there, as a compositor's virtual one, is
/// named by GDK's maker and model and its connector, as GNOME names one without a serial. The first is the primary one
/// where the desktop says none.
/// </summary>
internal static class GdkMonitors
{
    private const string Drm = "/sys/class/drm";

    public static IReadOnlyList<Gdk.Monitor> All()
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is not { } list)
        {
            return [];
        }

        return [.. Enumerable.Range(0, (int)list.GetNItems()).Select(at => list.GetObject((uint)at)).OfType<Gdk.Monitor>()];
    }

    public static IReadOnlyList<ShellMonitor> Read(IReadOnlyList<Gdk.Monitor> monitors, Func<Gdk.Monitor, PanelRoom> room, Gdk.Monitor? primary)
    {
        var ids = MonitorIds.Unique([.. monitors.Select(monitor => (Name(monitor), monitor.GetConnector() ?? ""))]);
        var first = primary ?? (monitors.Count > 0 ? monitors[0] : null);
        return [.. monitors.Select((monitor, at) =>
        {
            var place = monitor.Geometry;
            var kept = room(monitor);
            return new ShellMonitor(place.X, place.Y, place.Width, place.Height, monitor.GetScale(), kept.Top, monitor == first, FullScreen: false)
            {
                Id = ids[at],
                Room = kept,
            };
        })];
    }

    private static string Name(Gdk.Monitor monitor)
    {
        var connector = monitor.GetConnector() ?? "monitor";
        if (MonitorIds.OfGnome(Edid(connector), connector) is { } named)
        {
            return named;
        }

        var (maker, model) = (monitor.GetManufacturer() ?? "", monitor.GetModel() ?? "");
        return maker.Length + model.Length > 0 ? MonitorIds.OfGnome(maker, model, "", connector) : connector;
    }

    private static byte[] Edid(string connector)
    {
        try
        {
            return Directory.EnumerateDirectories(Drm, $"card*-{connector}").Select(card => Path.Combine(card, "edid")).FirstOrDefault(File.Exists)
                is { } edid ? File.ReadAllBytes(edid) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
