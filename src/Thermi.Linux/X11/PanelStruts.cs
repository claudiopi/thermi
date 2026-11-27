using Thermi.Linux.Gnome;

namespace Thermi.Linux.X11;

/// <summary>
/// The room the panels keep on each monitor of an X11 desktop, from the struts they ask the window manager for, as EWMH
/// says: each a depth from an edge of the root window, over a span along it. The root window's single work area,
/// _NET_WORKAREA, takes every panel's room off every monitor, and tells no edge; it is read only where no client keeps a
/// strut, as for a window manager that lists none.
/// </summary>
internal static class PanelStruts
{
    /// <summary>The room on each monitor, in its logical pixels.</summary>
    public static Func<Gdk.Monitor, PanelRoom> Of(Gdk.Display display)
    {
        var (struts, width, height) = XWindows.Struts(display);
        return struts.Count == 0 ? FromWorkArea : monitor => RoomOn(monitor, struts, width, height);
    }

    private static PanelRoom FromWorkArea(Gdk.Monitor monitor)
    {
        var place = monitor.Geometry;
        var (x, y, width, height) = XWindows.WorkArea(monitor);
        return new PanelRoom(x - place.X, y - place.Y, place.X + place.Width - (x + width), place.Y + place.Height - (y + height));
    }

    // The struts are in the root window's pixels, the monitor in logical ones, as GDK scales them.
    private static PanelRoom RoomOn(Gdk.Monitor monitor, IReadOnlyList<long[]> struts, long width, long height)
    {
        var place = monitor.Geometry;
        var scale = monitor.GetScaleFactor();
        var (left, top) = ((long)place.X * scale, (long)place.Y * scale);
        var (right, bottom) = (left + ((long)place.Width * scale), top + ((long)place.Height * scale));
        double Kept(Func<long[], long> depth, long most)
        {
            return struts.Max(strut => Math.Clamp(depth(strut), 0, most)) / (double)scale;
        }

        return new PanelRoom(
            Kept(strut => Spans(strut[4], strut[5], top, bottom) ? strut[0] - left : 0, right - left),
            Kept(strut => Spans(strut[8], strut[9], left, right) ? strut[2] - top : 0, bottom - top),
            Kept(strut => Spans(strut[6], strut[7], top, bottom) ? right - (width - strut[1]) : 0, right - left),
            Kept(strut => Spans(strut[10], strut[11], left, right) ? bottom - (height - strut[3]) : 0, bottom - top));
    }

    // Whether a strut's span, its first and last pixel along its edge, meets the monitor's, from start to just before end.
    private static bool Spans(long first, long last, long start, long end) => first < end && last >= start;
}
