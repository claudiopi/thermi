namespace Thermi.Linux.Tests.Desktops;

/// <summary>The edge of the primary monitor a panel keeps its room along: Xfce's, or the tests' own.</summary>
internal enum PanelEdge
{
    Top,
    Bottom,
    Left,
}

/// <summary>
/// A panel along an edge of the first monitor, the bottom as KDE Plasma 6 starts, the top or the left: a layer surface that
/// keeps its depth off the work area, for the compositors that start bare, where an overlay shows above a panel and snaps
/// along or beside it. With it, or alone where the compositor has a bar of its own, a pixel that tells when the compositor
/// drew what its clients sent before (<see cref="FramedAsync"/>).
/// </summary>
internal static class TestPanel
{
    /// <summary>The panel's depth from its edge, in logical pixels.</summary>
    public const int Height = 40;

    // GTK 4 from Python, with gtk4-layer-shell, which must load before the Wayland library does.
    private const string Code = """
        import sys, gi
        gi.require_version('Gtk', '4.0')
        gi.require_version('Gtk4LayerShell', '1.0')
        from gi.repository import Gdk, Gio, GLib, Gtk, Gtk4LayerShell as Layer

        # A pixel of the overlay layer, drawn transparent, above every window: each line on the standard input asks for
        # frames of it, and it says "framed" once the compositor drew three.
        def framer(app):
            pixel = Gtk.Window(application=app, decorated=False)
            Layer.init_for_window(pixel)
            Layer.set_layer(pixel, Layer.Layer.OVERLAY)
            Layer.set_anchor(pixel, Layer.Edge.TOP, True)
            Layer.set_anchor(pixel, Layer.Edge.LEFT, True)
            pixel.set_default_size(1, 1)
            css = Gtk.CssProvider()
            css.load_from_string('window.pixel { background: none; }')
            Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css, 800)
            pixel.add_css_class('pixel')
            pixel.present()
            def asked(*_):
                sys.stdin.readline()
                ticks = []
                def tick(widget, clock):
                    ticks.append(True)
                    if len(ticks) < 3: return True
                    print('framed', flush=True)
                    return False
                pixel.add_tick_callback(tick)
                return True
            GLib.io_add_watch(0, GLib.IO_IN, asked)

        def activate(app):
            framer(app)
            edge = sys.argv[1]
            if edge == 'pixel':
                print('ready', flush=True)
                return
            panel = Gtk.ApplicationWindow(application=app)
            Layer.init_for_window(panel)
            Layer.set_layer(panel, Layer.Layer.TOP)
            Layer.set_monitor(panel, Gdk.Display.get_default().get_monitors().get_item(0))
            along = {'top': ('TOP', 'LEFT', 'RIGHT'), 'bottom': ('BOTTOM', 'LEFT', 'RIGHT'), 'left': ('LEFT', 'TOP', 'BOTTOM')}[edge]
            for side in along:
                Layer.set_anchor(panel, getattr(Layer.Edge, side), True)
            panel.set_default_size(*((40, -1) if edge == 'left' else (-1, 40)))
            Layer.auto_exclusive_zone_enable(panel)
            panel.connect('map', lambda *_: print('ready', flush=True))
            panel.present()

        app = Gtk.Application(application_id='org.example.Panel', flags=Gio.ApplicationFlags.NON_UNIQUE)
        app.connect('activate', activate)
        app.run([])
        """;

    /// <summary>Shows the panel along <paramref name="panel"/>, or only its pixel for none, and returns once the compositor mapped it.</summary>
    public static async Task<SessionProcess> ShowAsync(OwnSession session, PanelEdge? panel, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "panel.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var preload = new Dictionary<string, string> { ["LD_PRELOAD"] = "libgtk4-layer-shell.so.0", ["GDK_BACKEND"] = "wayland" };
        var edge = panel switch
        {
            PanelEdge.Top => "top",
            PanelEdge.Bottom => "bottom",
            PanelEdge.Left => "left",
            _ => "pixel",
        };
        var shown = await session.StartAsync("python3", [script, edge], preload, cancel).ConfigureAwait(false);
        return await shown.ReadyAsync(cancel).ConfigureAwait(false) == "ready"
            ? shown
            : throw new InvalidOperationException($"The panel never showed: {shown.Error.Text}");
    }

    /// <summary>
    /// Returns once the compositor drew three frames from now: KWin and Sway nested in X11 put a frame on the X server's
    /// screen only after they told the client, so a screenshot taken at once can miss what a client just showed.
    /// </summary>
    public static async Task FramedAsync(SessionProcess panel, CancellationToken cancel)
    {
        await panel.Input.WriteLineAsync("frames").ConfigureAwait(false);
        await panel.Input.FlushAsync(cancel).ConfigureAwait(false);
        var said = await panel.Out.ReadLineAsync(cancel).ConfigureAwait(false);
        _ = said == "framed" ? 0 : throw new InvalidOperationException($"The panel drew no frame: {panel.Error.Text}");
    }
}
