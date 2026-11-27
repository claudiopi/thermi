using Thermi.Core.Placement;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// The monitors as GTK finds them on a desktop other than GNOME, in the stage's logical pixels: each named by its maker
/// and model, and its connector when two are alike, so a snap follows its monitor as the monitors move. The first is the
/// primary one where the desktop says none.
/// </summary>
internal static class GdkMonitors
{
    public static IReadOnlyList<Gdk.Monitor> All()
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is not { } list)
        {
            return [];
        }

        return [.. Enumerable.Range(0, (int)list.GetNItems()).Select(at => list.GetObject((uint)at)).OfType<Gdk.Monitor>()];
    }

    /// <summary>
    /// Each monitor as the overlays place themselves on it, its bar the logical pixels along its top that
    /// <paramref name="bar"/> says the panels keep, and <paramref name="primary"/> the primary one.
    /// </summary>
    public static IReadOnlyList<ShellMonitor> Read(IReadOnlyList<Gdk.Monitor> monitors, Func<Gdk.Monitor, double> bar, Gdk.Monitor? primary)
    {
        var ids = MonitorIds.Unique([.. monitors.Select(monitor => (Name(monitor), monitor.GetConnector() ?? ""))]);
        var first = primary ?? (monitors.Count > 0 ? monitors[0] : null);
        return [.. monitors.Select((monitor, at) =>
        {
            var place = monitor.Geometry;
            return new ShellMonitor(place.X, place.Y, place.Width, place.Height, monitor.GetScale(), bar(monitor), monitor == first, FullScreen: false)
            {
                Id = ids[at],
            };
        })];
    }

    // A monitor without a maker's or a model's name, as a virtual one, is named by its connector alone.
    private static string Name(Gdk.Monitor monitor) =>
        $"{monitor.GetManufacturer()}-{monitor.GetModel()}" is var name && name != "-" ? name : monitor.GetConnector() ?? "monitor";
}
