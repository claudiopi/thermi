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

    // The atoms' type, XA_ATOM, and a property written whole.
    private const nuint AtomType = 4;
    private const int Replace = 0;

    // An EWMH client message: its type, ClientMessage, and the masks the window manager listens with on the root window.
    private const int ClientMessage = 33;
    private const nint Redirect = (1 << 19) | (1 << 20);

    // XWMHints' InputHint, set without input: the window manager gives the window no keyboard on a click.
    private const nint InputHint = 1;

    // XSetInputFocus's revert-to RevertToParent, at CurrentTime.
    private const int ToParent = 2;

    /// <summary>
    /// The window as a utility one kept above the others, off the taskbar and the pager, given no keyboard on a click or
    /// as it maps; said once it is realized and before it maps, as the window manager reads it then.
    /// </summary>
    public static void Realized(Gdk.Surface surface)
    {
        var (display, window) = Of(surface);
        SkipTaskbar(surface.Handle, 1);
        SkipPager(surface.Handle, 1);
        UserTime(surface.Handle, 0);
        var utility = InternAtom(display, "_NET_WM_WINDOW_TYPE_UTILITY", 0);
        ChangeProperty(display, window, InternAtom(display, "_NET_WM_WINDOW_TYPE", 0), AtomType, 32, Replace, ref utility, 1);
        var hints = GetWMHints(display, window);
        if (hints != 0)
        {
            Marshal.WriteIntPtr(hints, Marshal.ReadIntPtr(hints) | InputHint);
            Marshal.WriteInt32(hints, IntPtr.Size, 0);
            _ = SetWMHints(display, window, hints);
            _ = Free(hints);
        }
    }

    /// <summary>The window above the others, asked of the window manager once it maps, as GTK writes its state then.</summary>
    public static void Mapped(Gdk.Surface surface)
    {
        var (display, window) = Of(surface);
        // XClientMessageEvent on a 64-bit machine: type, serial, send_event, display, window, message_type, format, data.
        Span<byte> message = stackalloc byte[192];
        message.Clear();
        MemoryMarshal.Write(message, ClientMessage);
        MemoryMarshal.Write(message[16..], 1);
        MemoryMarshal.Write(message[32..], window);
        MemoryMarshal.Write(message[40..], InternAtom(display, "_NET_WM_STATE", 0));
        MemoryMarshal.Write(message[48..], 32);
        MemoryMarshal.Write(message[56..], (nint)1);
        MemoryMarshal.Write(message[64..], (nint)InternAtom(display, "_NET_WM_STATE_ABOVE", 0));
        MemoryMarshal.Write(message[80..], (nint)1);
        _ = SendEvent(display, DefaultRootWindow(display), 0, Redirect, message);
        _ = Flush(display);
    }

    /// <summary>Moves the window's corner to a pixel of the root window.</summary>
    public static void Move(Gdk.Surface surface, int x, int y)
    {
        var (display, window) = Of(surface);
        _ = MoveWindow(display, window, x, y);
        _ = Flush(display);
    }

    /// <summary>Takes the keyboard to the window, and the window it came from, to give it back.</summary>
    public static nuint Focus(Gdk.Surface surface)
    {
        var (display, window) = Of(surface);
        _ = GetInputFocus(display, out var before, out _);
        _ = SetInputFocus(display, window, ToParent, 0);
        _ = Flush(display);
        return before;
    }

    /// <summary>Gives the keyboard back to <paramref name="window"/>.</summary>
    public static void Unfocus(Gdk.Surface surface, nuint window)
    {
        var display = DisplayOf(surface);
        _ = SetInputFocus(display, window, ToParent, 0);
        _ = Flush(display);
    }

    /// <summary>The pointer on the root window, in its pixels, and the keys held.</summary>
    public static (int X, int Y, uint Keys)? Pointer(Gdk.Display display)
    {
        var x = DisplayPointer(display);
        return QueryPointer(x, DefaultRootWindow(x), out _, out _, out var rootX, out var rootY, out _, out _, out var keys) != 0
            ? (rootX, rootY, keys)
            : null;
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

    [LibraryImport(Xlib, EntryPoint = "XSendEvent")]
    private static partial int SendEvent(nint display, nuint window, int propagate, nint mask, ReadOnlySpan<byte> message);

    [LibraryImport(Xlib, EntryPoint = "XMoveWindow")]
    private static partial int MoveWindow(nint display, nuint window, int x, int y);

    [LibraryImport(Xlib, EntryPoint = "XGetInputFocus")]
    private static partial int GetInputFocus(nint display, out nuint window, out int revert);

    [LibraryImport(Xlib, EntryPoint = "XSetInputFocus")]
    private static partial int SetInputFocus(nint display, nuint window, int revert, nuint time);

    [LibraryImport(Xlib, EntryPoint = "XQueryPointer")]
    private static partial int QueryPointer(
        nint display, nuint window, out nuint root, out nuint child, out int rootX, out int rootY, out int x, out int y, out uint keys);

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
}
