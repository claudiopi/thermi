using Thermi.Linux.LayerShell;

namespace Thermi.Linux.Desktop;

/// <summary>Where a desktop shows thermi's overlays (docs/adr/0019-linux.md).</summary>
internal enum SurfaceKind
{
    /// <summary>GNOME Shell, on Wayland or X11, through thermi's extension.</summary>
    Shell,

    /// <summary>A Wayland compositor with layer surfaces: KDE Plasma, Sway, Hyprland and the other wlroots ones.</summary>
    Layer,

    /// <summary>An X11 window manager, through windows kept above the others.</summary>
    X11,

    /// <summary>A Wayland compositor with neither: nothing shows, and thermi says why.</summary>
    None,

    /// <summary>A Wayland compositor other than GNOME's where gtk4-layer-shell is not installed: nothing shows, and thermi says why.</summary>
    NoLayerShell,

    /// <summary>GTK would not start, with no display or a broken one: no desktop is there to show anything on.</summary>
    NoDisplay,
}

/// <summary>Which desktop the session runs, as thermi's overlay and <c>thermi-cli</c> both tell it.</summary>
internal static class DesktopSession
{
    /// <summary>
    /// GNOME by its Shell on the session's bus, never by the desktop the session names: Budgie and GNOME Flashback name GNOME
    /// too and run no Shell, whose extension would never come. GTK is left alone there, as the extension shows the overlays.
    /// The Shell owns its name before it tells systemd it is ready, so before graphical-session.target starts the overlay.
    /// </summary>
    public static bool IsGnome() => SessionBus.HasOwner("org.gnome.Shell");

    /// <summary>
    /// GirCore's libraries that paint: each binds its library as its module starts, and nothing of theirs runs before,
    /// so the agents' commands run on a server with none.
    /// </summary>
    public static void Paints()
    {
        Gio.Module.Initialize();
        PangoCairo.Module.Initialize();
        Gdk.Module.Initialize();
    }

    /// <summary>
    /// Starts what the overlay paints with, and says the session's surfaces: GNOME's extension, else GTK's display, Wayland
    /// where its compositor serves one and X11 otherwise. GTK starts here, gtk4-layer-shell loaded before it, as it must
    /// be to take GTK's Wayland calls.
    /// </summary>
    public static SurfaceKind Start()
    {
        Gio.Module.Initialize();
        if (IsGnome())
        {
            Paints();
            return SurfaceKind.Shell;
        }

        var layer = LayerShellLibrary.Load();
        Paints();
        // A screen reader names the program by it, as it reads the overlay's menu.
        GLib.Functions.SetPrgname("thermi");
        Gtk.Module.Initialize();
        if (!Gtk.Functions.InitCheck() || Gdk.Display.GetDefault() is not { } display)
        {
            return SurfaceKind.NoDisplay;
        }

        if (!Wayland(display))
        {
            return SurfaceKind.X11;
        }

        return !layer ? SurfaceKind.NoLayerShell : LayerShellLibrary.Supported() ? SurfaceKind.Layer : SurfaceKind.None;
    }

    /// <summary>
    /// Whether GTK's display is a Wayland compositor's: an X server's name holds its display's number after a colon, ":0"
    /// or "host:0.0", and a Wayland socket's holds none.
    /// </summary>
    public static bool Wayland(Gdk.Display display) => !display.GetName().Contains(':', StringComparison.Ordinal);
}
