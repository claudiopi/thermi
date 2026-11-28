using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Thermi.Core.Placement;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Desktop;

/// <summary>
/// The monitors as GTK finds them on a desktop other than GNOME, in the stage's logical pixels: each named as GNOME's
/// display config names it, by its EDID's maker, product and serial (<see cref="MonitorIds.OfGnome(ReadOnlySpan{byte}, string)"/>),
/// so a snap follows its monitor as the monitors move and as the desktop changes. GDK tells no serial, on Wayland or X11:
/// X11's surfaces read the EDID RandR keeps of the output, and Wayland's the kernel's, of the one connected connector of the
/// monitor's name, as two GPUs may each have a connector of that name. A monitor without one, as a compositor's virtual
/// one, is named by GDK's maker and model and its connector, as GNOME names one without a serial. A name read from the EDID
/// is kept, as GDK makes a monitor anew for one that comes again; one without is read again each time, as an EDID may not
/// be readable yet as its monitor comes. The first is the primary one where the desktop says none.
/// </summary>
internal static class GdkMonitors
{
    private const string Drm = "/sys/class/drm";

    private static readonly ConditionalWeakTable<Gdk.Monitor, string> Names = [];

    public static IReadOnlyList<Gdk.Monitor> All()
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is not { } list)
        {
            return [];
        }

        return [.. Enumerable.Range(0, (int)list.GetNItems()).Select(at => list.GetObject((uint)at)).OfType<Gdk.Monitor>()];
    }

    /// <summary>
    /// The monitors, with the room the panels keep on each, the primary one, which a window covers full screen where
    /// <paramref name="fullScreen"/> says, none where it is null, and each one's EDID as <paramref name="edid"/> reads it,
    /// the kernel's where it is null. They are read on the main loop alone, as all of GTK is: one read beside it is logged.
    /// </summary>
    public static IReadOnlyList<ShellMonitor> Read(
        ILogger log, IReadOnlyList<Gdk.Monitor> monitors, Func<Gdk.Monitor, PanelRoom> room, Gdk.Monitor? primary,
        Func<Gdk.Monitor, bool>? fullScreen = null, Func<Gdk.Monitor, byte[]>? edid = null)
    {
        // A thread takes GLib's context only while no other runs its loop, or while it runs the loop itself.
        var context = GLib.MainContext.Default();
        if (context.Acquire())
        {
            context.Release();
        }
        else
        {
            log.MonitorsReadOffLoop(Environment.CurrentManagedThreadId);
        }

        var ids = MonitorIds.Unique([.. monitors.Select(monitor => (Name(monitor, edid ?? Edid), monitor.GetConnector() ?? ""))]);
        var first = primary ?? (monitors.Count > 0 ? monitors[0] : null);
        return [.. monitors.Select((monitor, at) =>
        {
            var place = monitor.Geometry;
            var kept = room(monitor);
            var covered = fullScreen?.Invoke(monitor) ?? false;
            return new ShellMonitor(place.X, place.Y, place.Width, place.Height, monitor.GetScale(), kept.Top, monitor == first, covered)
            {
                Id = ids[at],
                Room = kept,
            };
        })];
    }

    private static string Name(Gdk.Monitor monitor, Func<Gdk.Monitor, byte[]> edid)
    {
        if (Names.TryGetValue(monitor, out var known))
        {
            return known;
        }

        var connector = monitor.GetConnector() ?? "monitor";
        if (MonitorIds.OfGnome(edid(monitor), connector) is { } named)
        {
            Names.AddOrUpdate(monitor, named);
            return named;
        }

        var (maker, model) = (monitor.GetManufacturer() ?? "", monitor.GetModel() ?? "");
        return maker.Length + model.Length > 0 ? MonitorIds.OfGnome(maker, model, "", connector) : connector;
    }

    // The kernel's EDID of the one connector of the monitor's name that is connected; none where none is, or more are.
    private static byte[] Edid(Gdk.Monitor monitor)
    {
        if (monitor.GetConnector() is not { Length: > 0 } connector)
        {
            return [];
        }

        try
        {
            var connected = Directory.EnumerateDirectories(Drm, $"card*-{connector}")
                .Where(card => File.Exists(Path.Combine(card, "status")) && File.ReadAllText(Path.Combine(card, "status")).Trim() == "connected")
                .Select(card => File.ReadAllBytes(Path.Combine(card, "edid")))
                .Where(edid => edid.Length > 0)
                .Take(2)
                .ToList();
            return connected.Count == 1 ? connected[0] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
