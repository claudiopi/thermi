using System.Diagnostics;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A window of another program, as a person's, that fills its monitor's work area or the whole monitor, and says on its
/// standard output when it shows and each click it gets: what lies under the overlay, to see a click pass through it.
/// </summary>
internal sealed class TestWindow
{
    // GTK 4 from Python, as every desktop here ships both: the window is no program of thermi's.
    private const string Code = """
        import sys, gi
        gi.require_version('Gtk', '4.0')
        from gi.repository import Gtk

        def activate(app):
            window = Gtk.ApplicationWindow(application=app, title='thermi-tests')
            area = Gtk.DrawingArea()
            click = Gtk.GestureClick(button=0)
            click.connect('pressed', lambda gesture, n, x, y: print(f'click {gesture.get_current_button()}', flush=True))
            area.add_controller(click)
            window.set_child(area)
            full = sys.argv[1] == 'full'
            ready = lambda *_: print('ready', flush=True) if window.get_mapped() and window.is_fullscreen() == full else None
            window.connect('notify::fullscreened', ready)
            window.connect('map', ready)
            window.fullscreen() if full else window.maximize()
            window.present()

        app = Gtk.Application(application_id='org.thermi.TestWindow')
        app.connect('activate', activate)
        app.run(None)
        """;

    private readonly Process _process;

    private TestWindow(Process process)
    {
        _process = process;
    }

    /// <summary>Opens the window and returns once it shows, maximized or full screen.</summary>
    public static async Task<TestWindow> OpenAsync(OwnSession session, bool fullScreen, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "window.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var process = await session.StartAsync("python3", [script, fullScreen ? "full" : "maximized"], cancel: cancel).ConfigureAwait(false);
        var window = new TestWindow(process);
        if (await window.NextAsync(cancel).ConfigureAwait(false) != "ready")
        {
            throw new InvalidOperationException($"The window never showed: {await process.StandardError.ReadToEndAsync(cancel).ConfigureAwait(false)}");
        }

        return window;
    }

    /// <summary>The next thing the window says, as <c>click 1</c>, or null once it closed.</summary>
    public Task<string?> NextAsync(CancellationToken cancel) => _process.StandardOutput.ReadLineAsync(cancel).AsTask();
}
