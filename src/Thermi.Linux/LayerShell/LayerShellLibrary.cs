using System.Runtime.InteropServices;
using GObject.Internal;

namespace Thermi.Linux.LayerShell;

/// <summary>
/// A layer of the compositor's: the top one shows above every window, and under one gone full screen; the overlay one
/// above that too.
/// </summary>
internal enum Layer
{
    Background = 0,
    Top = 2,
    Overlay = 3,
}

/// <summary>An edge of a monitor a layer surface is anchored to and kept a margin from.</summary>
internal enum Edge
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3,
}

/// <summary>
/// gtk4-layer-shell, which makes a GTK window a layer surface of the compositor's (docs/adr/0019-linux.md). It has no
/// GirCore binding, so its few calls are declared here; it is loaded only where installed, and before GTK, as it takes
/// GTK's Wayland calls only when the dynamic linker finds it first.
/// </summary>
internal static partial class LayerShellLibrary
{
    private const string Library = "libgtk4-layer-shell.so.0";

    // Hyprland lets go of every button held as a surface takes the keyboard exclusively, which ends the press that took it
    // and the grab of the menu it opens; it gives the keyboard to one that takes it on demand once the pointer moves on it.
    private static readonly bool OnDemand = Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE") is { Length: > 0 };

    // dlopen's flags: resolve now, and give its symbols to every library loaded after, GTK's Wayland calls among them.
    private const int Now = 2;
    private const int Global = 0x100;

    private static bool _loaded;

    /// <summary>Loads the library where it is installed, once, before GTK; false where it is not.</summary>
    public static bool Load()
    {
        _loaded = _loaded || Open(Library, Now | Global) != 0;
        return _loaded;
    }

    /// <summary>Whether the compositor serves layer surfaces, once GTK runs on its Wayland display.</summary>
    public static bool Supported() => _loaded && IsSupported();

    public static void Init(Gtk.Window window) => InitForWindow(window.Handle);

    public static void SetLayer(Gtk.Window window, Layer layer) => SetLayer(window.Handle, (int)layer);

    public static void SetNamespace(Gtk.Window window, string name) => SetNamespace(window.Handle, name);

    public static void SetMonitor(Gtk.Window window, Gdk.Monitor monitor) => SetMonitor(window.Handle, monitor.Handle);

    public static void SetAnchor(Gtk.Window window, Edge edge, bool anchored) => SetAnchor(window.Handle, (int)edge, anchored ? 1 : 0);

    public static void SetMargin(Gtk.Window window, Edge edge, int margin) => SetMargin(window.Handle, (int)edge, margin);

    /// <summary>-1 to be placed on the whole monitor, over the panels' zones; 0 to be kept off them.</summary>
    public static void SetExclusiveZone(Gtk.Window window, int zone) => SetExclusiveZone(window.Handle, zone);

    /// <summary>The keyboard only while <paramref name="taken"/>, as a press holds the overlay; none otherwise.</summary>
    public static void SetKeyboard(Gtk.Window window, bool taken) => SetKeyboardMode(window.Handle, !taken ? 0 : OnDemand ? 2 : 1);

    [LibraryImport("libc", EntryPoint = "dlopen", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Open(string file, int flags);

    [LibraryImport(Library, EntryPoint = "gtk_layer_is_supported")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial bool IsSupported();

    [LibraryImport(Library, EntryPoint = "gtk_layer_init_for_window")]
    private static partial void InitForWindow(ObjectHandle window);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_layer")]
    private static partial void SetLayer(ObjectHandle window, int layer);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_namespace", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void SetNamespace(ObjectHandle window, string name);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_monitor")]
    private static partial void SetMonitor(ObjectHandle window, ObjectHandle monitor);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_anchor")]
    private static partial void SetAnchor(ObjectHandle window, int edge, int anchored);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_margin")]
    private static partial void SetMargin(ObjectHandle window, int edge, int margin);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_exclusive_zone")]
    private static partial void SetExclusiveZone(ObjectHandle window, int zone);

    [LibraryImport(Library, EntryPoint = "gtk_layer_set_keyboard_mode")]
    private static partial void SetKeyboardMode(ObjectHandle window, int mode);
}
