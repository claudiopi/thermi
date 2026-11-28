using System.Runtime.InteropServices;
using GObject.Internal;

namespace Thermi.Linux.X11;

/// <summary>
/// What GTK 4 no longer does for a window on X11, said to the window manager as EWMH asks, through Xlib and GDK's own X11
/// calls (docs/adr/0019-linux.md). Neither has a GirCore binding, so their few calls are declared here; both are loaded
/// only on an X11 display, where GTK itself loaded them.
/// </summary>
internal static partial class XWindows
{
    private const string Xlib = "libX11.so.6";
    private const string Gdk = "libgtk-4.so.1";
    private const string Xext = "libXext.so.6";

    // XShapeCombineRectangles' kind, ShapeInput, and its operation, ShapeSet, with rectangles in no order.
    private const int ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;

    // The atoms' type, XA_ATOM, and a property written whole.
    private const nuint AtomType = 4;
    private const int Replace = 0;

    // XWMHints' InputHint, set without input: the window manager gives the window no keyboard on a click.
    private const nint InputHint = 1;

    // XSetInputFocus's revert-to RevertToParent, at CurrentTime.
    private const int ToParent = 2;

    // _NET_WM_STRUT_PARTIAL's items: each edge's depth, then each edge's span along it, its first and last pixel.
    private const int Partial = 12;

    // XGetWindowProperty's AnyPropertyType, and the most items read of a property, past any client list's.
    private const nuint AnyType = 0;
    private const nint MostItems = 65536;

    /// <summary>
    /// The window as a dock, which the window manager keeps above the others, off the taskbar and the pager, given no
    /// keyboard on a click or as it maps; said once it is realized and before it maps, as the window manager reads it then.
    /// </summary>
    public static void Realized(Gdk.Surface surface)
    {
        var (display, window) = Of(surface);
        SkipTaskbar(surface.Handle, 1);
        SkipPager(surface.Handle, 1);
        UserTime(surface.Handle, 0);
        var dock = InternAtom(display, "_NET_WM_WINDOW_TYPE_DOCK", 0);
        ChangeProperty(display, window, InternAtom(display, "_NET_WM_WINDOW_TYPE", 0), AtomType, 32, Replace, ref dock, 1);
        var hints = GetWMHints(display, window);
        if (hints != 0)
        {
            Marshal.WriteIntPtr(hints, Marshal.ReadIntPtr(hints) | InputHint);
            Marshal.WriteInt32(hints, IntPtr.Size, 0);
            _ = SetWMHints(display, window, hints);
            _ = Free(hints);
        }
    }

    /// <summary>The window takes the pointer only on <paramref name="boxes"/>, in its logical pixels, and nowhere when none.</summary>
    public static void Shape(Gdk.Surface surface, IReadOnlyList<(int X, int Y, int Width, int Height)> boxes)
    {
        var (display, window) = Of(surface);
        var scale = surface.GetScaleFactor();
        XRectangle[] rectangles = [.. boxes.Select(box => new XRectangle
        {
            X = (short)(box.X * scale),
            Y = (short)(box.Y * scale),
            Width = (ushort)(box.Width * scale),
            Height = (ushort)(box.Height * scale),
        })];
        ShapeRectangles(display, window, ShapeInput, 0, 0, rectangles, rectangles.Length, ShapeSet, Unsorted);
        _ = Flush(display);
    }

    /// <summary>
    /// Moves the window's corner to a logical pixel of the stage, on the root window's pixel nearest it, and raises it
    /// above thermi's other windows where <paramref name="raise"/>, as a dragged overlay rises above the snap marks.
    /// </summary>
    public static void Move(Gdk.Surface surface, double x, double y, bool raise)
    {
        var (display, window) = Of(surface);
        var scale = surface.GetScaleFactor();
        _ = MoveWindow(display, window, (int)Math.Round(x * scale), (int)Math.Round(y * scale));
        if (raise)
        {
            _ = RaiseWindow(display, window);
        }

        _ = Flush(display);
    }

    /// <summary>
    /// Takes the keyboard to the window, and the window it came from, to give it back. A window that is gone or not shown
    /// takes no focus, which the X server answers with an error that GDK would end the process for: GDK ignores it here.
    /// </summary>
    public static nuint Focus(Gdk.Surface surface)
    {
        var (display, window) = Of(surface);
        var gdk = surface.GetDisplay();
        TrapPush(gdk.Handle);
        _ = GetInputFocus(display, out var before, out _);
        _ = SetInputFocus(display, window, ToParent, 0);
        _ = Flush(display);
        TrapPopIgnored(gdk.Handle);
        return before;
    }

    /// <summary>Gives the keyboard back to <paramref name="window"/>, which may have gone since, as <see cref="Focus"/> takes it.</summary>
    public static void Unfocus(Gdk.Surface surface, nuint window)
    {
        var display = DisplayOf(surface);
        var gdk = surface.GetDisplay();
        TrapPush(gdk.Handle);
        _ = SetInputFocus(display, window, ToParent, 0);
        _ = Flush(display);
        TrapPopIgnored(gdk.Handle);
    }

    /// <summary>The pointer on the root window, in the stage's logical pixels, and the keys held.</summary>
    public static (double X, double Y, uint Keys)? Pointer(Gdk.Display display)
    {
        var x = DisplayPointer(display);
        var scale = (double)Scale(display);
        return QueryPointer(x, DefaultRootWindow(x), out _, out _, out var rootX, out var rootY, out _, out _, out var keys) != 0
            ? (rootX / scale, rootY / scale, keys)
            : null;
    }

    /// <summary>
    /// The struts the window manager's clients keep, each as _NET_WM_STRUT_PARTIAL's twelve, and the root window's size,
    /// all in its pixels. A client with only _NET_WM_STRUT keeps the whole length of each edge; one gone while it is read
    /// keeps nothing.
    /// </summary>
    public static (IReadOnlyList<long[]> Struts, long Width, long Height) Struts(Gdk.Display display)
    {
        var x = DisplayPointer(display);
        var screen = DefaultScreen(x);
        var (width, height) = ((long)DisplayWidth(x, screen), (long)DisplayHeight(x, screen));
        TrapPush(display.Handle);
        try
        {
            var struts = Longs(x, DefaultRootWindow(x), "_NET_CLIENT_LIST").Select(client =>
            {
                var partial = Longs(x, (nuint)client, "_NET_WM_STRUT_PARTIAL");
                var whole = partial.Length >= Partial ? [] : Longs(x, (nuint)client, "_NET_WM_STRUT");
                return partial.Length >= Partial ? partial[..Partial]
                    : whole.Length >= 4 ? [.. whole[..4], 0, height - 1, 0, height - 1, 0, width - 1, 0, width - 1]
                    : null;
            });
            return ([.. struts.OfType<long[]>()], width, height);
        }
        finally
        {
            TrapPopIgnored(display.Handle);
        }
    }

    /// <summary>The scale of GDK's logical pixels on X11, one for every monitor.</summary>
    public static int Scale(Gdk.Display display) => display.GetMonitors().GetObject(0) is Gdk.Monitor first ? first.GetScaleFactor() : 1;

    // A property of 32-bit items, which Xlib hands as longs; none where the window has none.
    private static long[] Longs(nint display, nuint window, string name)
    {
        var property = InternAtom(display, name, 0);
        if (GetWindowProperty(display, window, property, 0, MostItems, 0, AnyType, out _, out var format, out var count, out _, out var data) != 0
            || data == 0)
        {
            return [];
        }

        try
        {
            return format == 32 ? [.. Enumerable.Range(0, (int)count).Select(at => (long)Marshal.ReadIntPtr(data, at * IntPtr.Size))] : [];
        }
        finally
        {
            _ = Free(data);
        }
    }

    /// <summary>The primary monitor, as RandR says, GDK's own object for it, or null for none.</summary>
    public static Gdk.Monitor? Primary(Gdk.Display display) =>
        PrimaryMonitor(display.Handle) is var primary and not 0 ? (Gdk.Monitor)InstanceWrapper.WrapHandle<Gdk.Monitor>(primary, ownedRef: false) : null;

    /// <summary>The monitor's room past the panels' struts, as the window manager says it, in logical pixels.</summary>
    public static (int X, int Y, int Width, int Height) WorkArea(Gdk.Monitor monitor)
    {
        WorkArea(monitor.Handle, out var area);
        return (area.X, area.Y, area.Width, area.Height);
    }

    private static (nint Display, nuint Window) Of(Gdk.Surface surface) => (DisplayOf(surface), Xid(surface.Handle));

    private static nint DisplayOf(Gdk.Surface surface) => DisplayPointer(surface.GetDisplay());

    private static nint DisplayPointer(Gdk.Display display) => XDisplay(display.Handle);


    [LibraryImport(Gdk, EntryPoint = "gdk_x11_display_get_xdisplay")]
    private static partial nint XDisplay(ObjectHandle display);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_surface_get_xid")]
    private static partial nuint Xid(ObjectHandle surface);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_surface_set_skip_taskbar_hint")]
    private static partial void SkipTaskbar(ObjectHandle surface, int skip);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_surface_set_skip_pager_hint")]
    private static partial void SkipPager(ObjectHandle surface, int skip);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_surface_set_user_time")]
    private static partial void UserTime(ObjectHandle surface, uint time);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_display_get_primary_monitor")]
    private static partial nint PrimaryMonitor(ObjectHandle display);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_monitor_get_workarea")]
    private static partial void WorkArea(ObjectHandle monitor, out Rectangle area);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_monitor_get_output")]
    private static partial nuint Output(ObjectHandle monitor);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_display_error_trap_push")]
    private static partial void TrapPush(ObjectHandle display);

    [LibraryImport(Gdk, EntryPoint = "gdk_x11_display_error_trap_pop_ignored")]
    private static partial void TrapPopIgnored(ObjectHandle display);

    [LibraryImport(Xlib, EntryPoint = "XDefaultScreen")]
    private static partial int DefaultScreen(nint display);

    [LibraryImport(Xlib, EntryPoint = "XDisplayWidth")]
    private static partial int DisplayWidth(nint display, int screen);

    [LibraryImport(Xlib, EntryPoint = "XDisplayHeight")]
    private static partial int DisplayHeight(nint display, int screen);

    [LibraryImport(Xlib, EntryPoint = "XGetWindowProperty")]
    private static partial int GetWindowProperty(
        nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint type,
        out nuint actualType, out int format, out nuint count, out nuint after, out nint data);

    [LibraryImport(Xlib, EntryPoint = "XRaiseWindow")]
    private static partial int RaiseWindow(nint display, nuint window);

    [LibraryImport(Xlib, EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint InternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Xlib, EntryPoint = "XChangeProperty")]
    private static partial int ChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, ref nuint data, int count);

    [LibraryImport(Xlib, EntryPoint = "XGetWMHints")]
    private static partial nint GetWMHints(nint display, nuint window);

    [LibraryImport(Xlib, EntryPoint = "XSetWMHints")]
    private static partial int SetWMHints(nint display, nuint window, nint hints);

    [LibraryImport(Xlib, EntryPoint = "XFree")]
    private static partial int Free(nint data);

    [LibraryImport(Xlib, EntryPoint = "XDefaultRootWindow")]
    private static partial nuint DefaultRootWindow(nint display);

    [LibraryImport(Xlib, EntryPoint = "XMoveWindow")]
    private static partial int MoveWindow(nint display, nuint window, int x, int y);

    [LibraryImport(Xlib, EntryPoint = "XGetInputFocus")]
    private static partial int GetInputFocus(nint display, out nuint window, out int revert);

    [LibraryImport(Xlib, EntryPoint = "XSetInputFocus")]
    private static partial int SetInputFocus(nint display, nuint window, int revert, nuint time);

    [LibraryImport(Xlib, EntryPoint = "XQueryPointer")]
    private static partial int QueryPointer(
        nint display, nuint window, out nuint root, out nuint child, out int rootX, out int rootY, out int x, out int y, out uint keys);

    [LibraryImport(Xlib, EntryPoint = "XGetGeometry")]
    private static partial int GetGeometry(
        nint display, nuint drawable, out nuint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);

    [LibraryImport(Xlib, EntryPoint = "XTranslateCoordinates")]
    private static partial int TranslateCoordinates(nint display, nuint from, nuint to, int x, int y, out int toX, out int toY, out nuint child);

    [LibraryImport(Xext, EntryPoint = "XShapeCombineRectangles")]
    private static partial void ShapeRectangles(
        nint display, nuint window, int kind, int x, int y, ReadOnlySpan<XRectangle> rectangles, int count, int operation, int ordering);

    [LibraryImport(Xlib, EntryPoint = "XFlush")]
    private static partial int Flush(nint display);

    // GdkRectangle, as GDK writes it.
    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    private struct XRectangle
    {
        public short X;
        public short Y;
        public ushort Width;
        public ushort Height;
    }
}
