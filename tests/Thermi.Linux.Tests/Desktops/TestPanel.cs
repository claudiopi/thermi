namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A panel along the top of the first monitor, as KDE Plasma's: a layer surface that keeps its height off the work area,
/// for the compositors that start bare, where an overlay shows above a panel and snaps along it.
/// </summary>
internal static class TestPanel
{
    /// <summary>The panel's height, in logical pixels.</summary>
    public const int Height = 40;

    // GTK 4 from Python, with gtk4-layer-shell, which must load before the Wayland library does.
    private const string Code = """
        import gi
        gi.require_version('Gtk', '4.0')
        gi.require_version('Gtk4LayerShell', '1.0')
        from gi.repository import Gdk, Gio, Gtk, Gtk4LayerShell as Layer

        def activate(app):
            panel = Gtk.ApplicationWindow(application=app)
            Layer.init_for_window(panel)
            Layer.set_layer(panel, Layer.Layer.TOP)
            Layer.set_monitor(panel, Gdk.Display.get_default().get_monitors().get_item(0))
            for edge in (Layer.Edge.TOP, Layer.Edge.LEFT, Layer.Edge.RIGHT):
                Layer.set_anchor(panel, edge, True)
            panel.set_default_size(-1, 40)
            Layer.auto_exclusive_zone_enable(panel)
            panel.connect('map', lambda *_: print('ready', flush=True))
            panel.present()

        app = Gtk.Application(application_id='org.example.Panel', flags=Gio.ApplicationFlags.NON_UNIQUE)
        app.connect('activate', activate)
        app.run(None)
        """;

    /// <summary>Shows the panel, and returns once the compositor mapped it.</summary>
    public static async Task ShowAsync(OwnSession session, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "panel.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var preload = new Dictionary<string, string> { ["LD_PRELOAD"] = "libgtk4-layer-shell.so.0", ["GDK_BACKEND"] = "wayland" };
        var panel = await session.StartAsync("python3", [script], preload, cancel).ConfigureAwait(false);
        if (await panel.ReadyAsync(cancel).ConfigureAwait(false) != "ready")
        {
            throw new InvalidOperationException($"The panel never showed: {panel.Error.Text}");
        }
    }
}
