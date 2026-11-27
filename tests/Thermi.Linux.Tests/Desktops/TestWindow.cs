namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A window of another program, as a person's, that fills its monitor's work area or the whole monitor, and says on its
/// standard output when it shows and each click it gets: what lies under the overlay, to see a click pass through it.
/// </summary>
internal sealed class TestWindow
{
    // GTK 4 from Python, as every desktop here ships both: the window is no program of thermi's, and names nothing of it. Each
    // is an application of its own, so a second one opens a window of its own instead of raising the first's.
    private const string Code = """
        import sys, gi
        gi.require_version('Gtk', '4.0')
        from gi.repository import Gio, Gtk

        def activate(app):
            window = Gtk.ApplicationWindow(application=app, title=sys.argv[2])
            area = Gtk.DrawingArea()
            click = Gtk.GestureClick(button=0)
            click.connect('pressed', lambda gesture, n, x, y: print(f'click {gesture.get_current_button()}', flush=True))
            area.add_controller(click)
            window.set_child(area)
            full = sys.argv[1] == 'full'
            said = []
            # Shown once the compositor showed a frame drawn at its size: a frame clock ticks again only after it did.
            def ready(*_):
                if said or not window.get_mapped() or not (window.is_fullscreen() if full else window.is_maximized()): return
                said.append(True)
                ticks = []
                def tick(widget, clock):
                    ticks.append(True)
                    if len(ticks) < 3: return True
                    print('ready', flush=True)
                    return False
                window.add_tick_callback(tick)
            window.connect('notify::fullscreened', ready)
            # The Shell on X11 may take a maximize back while it starts up: the window asks again each time.
            def again(*_):
                if not full and window.get_mapped() and not window.is_maximized(): window.maximize()
            window.connect('notify::maximized', again)
            window.connect('map', again)
            window.connect('notify::maximized', ready)
            window.connect('map', ready)
            window.fullscreen() if full else window.maximize()
            window.present()

        app = Gtk.Application(application_id='org.example.Underneath', flags=Gio.ApplicationFlags.NON_UNIQUE)
        app.connect('activate', activate)
        app.run(None)
        """;

    /// <summary>The window's title, as the desktop shows it.</summary>
    public const string Title = "underneath";

    private readonly SessionProcess _process;

    private TestWindow(SessionProcess process)
    {
        _process = process;
    }

    /// <summary>Opens the window and returns once it shows, maximized or full screen.</summary>
    public static async Task<TestWindow> OpenAsync(OwnSession session, bool fullScreen, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "window.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var process = await session.StartAsync("python3", [script, fullScreen ? "full" : "maximized", Title], cancel: cancel).ConfigureAwait(false);
        return await process.ReadyAsync(cancel).ConfigureAwait(false) == "ready"
            ? new TestWindow(process)
            : throw new InvalidOperationException($"The window never showed: {process.Error.Text}");
    }

    /// <summary>The next thing the window says that the test has not read yet, as <c>click 1</c>, or null once it closed.</summary>
    public Task<string?> NextAsync(CancellationToken cancel) => _process.Out.ReadLineAsync(cancel);
}
