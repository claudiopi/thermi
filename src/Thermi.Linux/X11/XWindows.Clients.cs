using System.Runtime.InteropServices;

namespace Thermi.Linux.X11;

/// <summary>
/// The other clients' windows, as the window manager lists them on the root window, and a monitor's EDID, as RandR keeps
/// it for each output: an X server's outputs need not be the kernel's connectors, as on a server of its own or another GPU's.
/// </summary>
internal static partial class XWindows
{
    private const string Xrandr = "libXrandr.so.2";

    // The EDID's longest, past its blocks of extensions, in 32-bit items, as XRRGetOutputProperty counts them.
    private const nint EdidItems = 1024;

    /// <summary>The X11 window of a surface of thermi's.</summary>
    public static uint Id(Gdk.Surface surface) => (uint)Xid(surface.Handle);

    /// <summary>The window the person works in, as the window manager says, or 0 for none.</summary>
    public static uint Active(Gdk.Display display)
    {
        var x = DisplayPointer(display);
        return Trapped(display, () => Longs(x, DefaultRootWindow(x), "_NET_ACTIVE_WINDOW") is [var active, ..] ? (uint)active : 0u);
    }

    /// <summary>
    /// Where the window the person works in covers, in the stage's logical pixels, where it is full screen; null where it is
    /// not, or none is. None of <paramref name="ours"/> is that window, as Windows never lets an overlay take the foreground.
    /// </summary>
    public static (double X, double Y, double Width, double Height)? FullScreen(Gdk.Display display, IReadOnlyCollection<uint> ours)
    {
        var x = DisplayPointer(display);
        var full = InternAtom(x, "_NET_WM_STATE_FULLSCREEN", 0);
        return Trapped<(double, double, double, double)?>(display, () =>
        {
            var front = InFront(x, ours);
            if (front == 0 || !Longs(x, front, "_NET_WM_STATE").Contains((long)full)
                || GetGeometry(x, front, out var root, out _, out _, out var width, out var height, out _, out _) == 0
                || TranslateCoordinates(x, front, root, 0, 0, out var left, out var top, out _) == 0)
            {
                return null;
            }

            double scale = Scale(display);
            return (left / scale, top / scale, width / scale, height / scale);
        });
    }

    // The active window, or the highest client that is no dock while one of ours is active: xfwm4 activates a dock as it
    // maps, input hint or not, and a press gives one the keyboard. Were ours the window in front, an overlay shown only in
    // full screen would hide as it showed, and show again as the full-screen window came back.
    private static nuint InFront(nint x, IReadOnlyCollection<uint> ours)
    {
        var root = DefaultRootWindow(x);
        var active = Longs(x, root, "_NET_ACTIVE_WINDOW") is [var first, ..] ? first : 0;
        if (!ours.Contains((uint)active))
        {
            return (nuint)active;
        }

        var dock = (long)InternAtom(x, "_NET_WM_WINDOW_TYPE_DOCK", 0);
        return (nuint)Longs(x, root, "_NET_CLIENT_LIST_STACKING")
            .LastOrDefault(window => !ours.Contains((uint)window) && !Longs(x, (nuint)window, "_NET_WM_WINDOW_TYPE").Contains(dock));
    }

    /// <summary>Whether a dock of another client's, as a panel, is stacked above any of <paramref name="ours"/>.</summary>
    public static bool Covered(Gdk.Display display, IReadOnlyCollection<uint> ours)
    {
        var x = DisplayPointer(display);
        var dock = (long)InternAtom(x, "_NET_WM_WINDOW_TYPE_DOCK", 0);
        return Trapped(display, () =>
        {
            var stacked = Longs(x, DefaultRootWindow(x), "_NET_CLIENT_LIST_STACKING");
            var lowest = Array.FindIndex(stacked, window => ours.Contains((uint)window));
            return lowest >= 0 && stacked[(lowest + 1)..]
                .Any(window => !ours.Contains((uint)window) && Longs(x, (nuint)window, "_NET_WM_WINDOW_TYPE").Contains(dock));
        });
    }

    /// <summary>Raises <paramref name="ours"/> above every other dock, in the order they stack now.</summary>
    public static void Raise(Gdk.Display display, IReadOnlyCollection<uint> ours)
    {
        var x = DisplayPointer(display);
        _ = Trapped(display, () =>
        {
            var stacked = Longs(x, DefaultRootWindow(x), "_NET_CLIENT_LIST_STACKING").Select(window => (uint)window).ToList();
            foreach (var window in ours.OrderBy(window => stacked.IndexOf(window)))
            {
                _ = RaiseWindow(x, window);
            }

            _ = Flush(x);
            return true;
        });
    }

    /// <summary>The monitor's EDID, as RandR keeps it of its output; none where it keeps none.</summary>
    public static byte[] Edid(Gdk.Monitor monitor)
    {
        var display = monitor.GetDisplay();
        var x = DisplayPointer(display);
        var output = Output(monitor.Handle);
        var edid = InternAtom(x, "EDID", 1);
        if (output == 0 || edid == 0)
        {
            return [];
        }

        return Trapped<byte[]>(display, () =>
        {
            if (OutputProperty(x, output, edid, 0, EdidItems, 0, 0, AnyType, out _, out var format, out var count, out _, out var data) != 0 || data == 0)
            {
                return [];
            }

            try
            {
                return format == 8 ? CopyOf(data, (int)count) : [];
            }
            finally
            {
                _ = Free(data);
            }
        });
    }

    private static byte[] CopyOf(nint data, int count)
    {
        var bytes = new byte[count];
        Marshal.Copy(data, bytes, 0, count);
        return bytes;
    }

    // A window may go between two reads, which the X server answers with an error that GDK would end the process for.
    private static T Trapped<T>(Gdk.Display display, Func<T> read)
    {
        TrapPush(display.Handle);
        try
        {
            return read();
        }
        finally
        {
            TrapPopIgnored(display.Handle);
        }
    }

    [LibraryImport(Xrandr, EntryPoint = "XRRGetOutputProperty")]
    private static partial int OutputProperty(
        nint display, nuint output, nuint property, nint offset, nint length, int delete, int pending, nuint type,
        out nuint actualType, out int format, out nuint count, out nuint after, out nint data);
}
