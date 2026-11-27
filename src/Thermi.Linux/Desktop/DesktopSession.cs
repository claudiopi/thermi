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
}

/// <summary>Which desktop the session runs, as thermi's overlay and <c>thermi-cli</c> both tell it.</summary>
internal static class DesktopSession
{
    /// <summary>
    /// GNOME by the desktop the session says, or by the Shell on its bus, as a session started by hand says nothing; GTK is
    /// left alone there, as the extension shows the overlays.
    /// </summary>
    public static bool IsGnome() =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':').Contains("GNOME", StringComparer.OrdinalIgnoreCase)
        || SessionBus.HasOwner("org.gnome.Shell");

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
        Gtk.Module.Initialize();
        if (!Gtk.Functions.InitCheck() || Gdk.Display.GetDefault() is not { } display)
        {
            return SurfaceKind.None;
        }

        if (!Wayland(display))
        {
            return SurfaceKind.X11;
        }

        return layer && LayerShellLibrary.Supported() ? SurfaceKind.Layer : SurfaceKind.None;
    }

    // An X server's name holds its display's number after a colon, ":0" or "host:0.0"; a Wayland socket's holds none.
    private static bool Wayland(Gdk.Display display) => !display.GetName().Contains(':', StringComparison.Ordinal);
}
