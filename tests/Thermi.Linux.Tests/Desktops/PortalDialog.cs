namespace Thermi.Linux.Tests.Desktops;

/// <summary>The dialog a desktop's portal shows when a program first asks it for something, as KDE Plasma's asks for hotkeys.</summary>
internal static class PortalDialog
{
    // Says "listening" once accessibility is on and it hears windows activate, then each one's program as it does.
    private const string Watch = """
        import gi
        gi.require_version('Atspi', '2.0')
        from gi.repository import Atspi, Gio, GLib
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        bus.call_sync('org.a11y.Bus', '/org/a11y/bus', 'org.freedesktop.DBus.Properties', 'Set',
                      GLib.Variant('(ssv)', ('org.a11y.Status', 'IsEnabled', GLib.Variant('b', True))),
                      None, Gio.DBusCallFlags.NONE, -1, None)
        def activated(event):
            application = event.source.get_application()
            print(application.get_name() if application else '', flush=True)
        listener = Atspi.EventListener.new(activated)
        listener.register('window:activate')
        print('listening', flush=True)
        Atspi.event_main()
        """;

    /// <summary>
    /// What ends once a dialog of the desktop's portal is in front, as accessibility tells of each window that activates.
    /// The listener turns accessibility on first, which Qt's dialogs follow, and listens once this returns.
    /// </summary>
    public static async Task<Task> AsksAsync(OwnSession session, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "portal.py");
        await File.WriteAllTextAsync(script, Watch, cancel).ConfigureAwait(false);
        var watch = await session.StartAsync("python3", [script], cancel: cancel).ConfigureAwait(false);
        await watch.ReadyAsync(cancel).ConfigureAwait(false);
        return watch.Out.NextAsync(line => line.Contains("portal", StringComparison.Ordinal), cancel);
    }
}
