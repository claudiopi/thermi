namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A tray of the test's own, as KDE Plasma's: it takes the session's <c>org.kde.StatusNotifierWatcher</c>, says each icon a
/// program registers, and reads an icon's properties and menu as a tray does.
/// </summary>
internal sealed class TrayWatcher
{
    // GLib from Python serves the watcher's interface, with a host registered, and writes each item as "<bus name> <path>".
    private const string Code = """
        import sys, gi
        from gi.repository import Gio, GLib
        XML = '''<node><interface name="org.kde.StatusNotifierWatcher">
          <method name="RegisterStatusNotifierItem"><arg type="s" direction="in"/></method>
          <method name="RegisterStatusNotifierHost"><arg type="s" direction="in"/></method>
          <property name="RegisteredStatusNotifierItems" type="as" access="read"/>
          <property name="IsStatusNotifierHostRegistered" type="b" access="read"/>
          <property name="ProtocolVersion" type="i" access="read"/>
          <signal name="StatusNotifierItemRegistered"><arg type="s"/></signal>
        </interface></node>'''
        items = []
        def call(bus, sender, path, interface, method, args, invocation):
            if method == 'RegisterStatusNotifierItem':
                item = args.unpack()[0]
                at = item if item.startswith('/') else '/StatusNotifierItem'
                name = sender if item.startswith('/') else item
                items.append(f'{name}{at}')
                print(name, at, flush=True)
            invocation.return_value(None)
        def get(bus, sender, path, interface, name):
            return {'RegisteredStatusNotifierItems': GLib.Variant('as', items), 'IsStatusNotifierHostRegistered': GLib.Variant('b', True),
                    'ProtocolVersion': GLib.Variant('i', 0)}[name]
        def bus_up(bus, name):
            bus.register_object('/StatusNotifierWatcher', Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call, get, None)
        Gio.bus_own_name(Gio.BusType.SESSION, 'org.kde.StatusNotifierWatcher', 0, bus_up, lambda *_: print('ready', flush=True), None)
        GLib.MainLoop().run()
        """;

    // Saves an icon's largest pixmap as a tray draws it: StatusNotifierItem's ARGB32, in network byte order, as an RGBA PNG.
    private const string Pixmap = """
        import sys, gi
        gi.require_version('GdkPixbuf', '2.0')
        from gi.repository import Gio, GLib, GdkPixbuf
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        pixmaps = bus.call_sync(sys.argv[1], sys.argv[2], 'org.freedesktop.DBus.Properties', 'Get',
                                GLib.Variant('(ss)', ('org.kde.StatusNotifierItem', 'IconPixmap')),
                                None, Gio.DBusCallFlags.NONE, -1, None).unpack()[0]
        width, height, argb = max(pixmaps, key=lambda pixmap: pixmap[0] * pixmap[1])
        argb = bytes(argb)
        rgba = bytearray()
        for at in range(0, len(argb), 4):
            rgba += bytes((argb[at + 1], argb[at + 2], argb[at + 3], argb[at]))
        GdkPixbuf.Pixbuf.new_from_bytes(GLib.Bytes.new(bytes(rgba)), GdkPixbuf.Colorspace.RGB, True, 8, width, height, width * 4) \
            .savev(sys.argv[3], 'png', [], [])
        """;

    private readonly OwnSession _session;
    private readonly SessionProcess _watcher;

    private TrayWatcher(OwnSession session, SessionProcess watcher)
    {
        _session = session;
        _watcher = watcher;
    }

    /// <summary>Serves the session's tray, and returns once it holds the watcher's name.</summary>
    public static async Task<TrayWatcher> ServeAsync(OwnSession session, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "tray.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var watcher = await session.StartAsync("python3", [script], cancel: cancel).ConfigureAwait(false);
        return await watcher.ReadyAsync(cancel).ConfigureAwait(false) == "ready"
            ? new TrayWatcher(session, watcher)
            : throw new InvalidOperationException("The tray never took the watcher's name.");
    }

    /// <summary>
    /// The next icon registered that the test has not read yet, in order, each once: its bus name and object path. An icon
    /// registers once, as it shows, and again only when its program starts again.
    /// </summary>
    public async Task<(string Name, string Path)> NextAsync(CancellationToken cancel)
    {
        var line = await _watcher.Out.ReadLineAsync(cancel).ConfigureAwait(false) ?? throw new InvalidOperationException("The tray ended.");
        var parts = line.Split(' ');
        return (parts[0], parts[1]);
    }

    /// <summary>A property of an icon, as gdbus prints its value.</summary>
    public async Task<string> PropertyAsync((string Name, string Path) item, string property, CancellationToken cancel) =>
        (await _session.RunAsync(
            "gdbus",
            ["call", "--session", "--dest", item.Name, "--object-path", item.Path, "--method", "org.freedesktop.DBus.Properties.Get",
                "org.kde.StatusNotifierItem", property],
            cancel: cancel).ConfigureAwait(false)).Out.Trim();

    /// <summary>The icon's picture, as a tray shows it, saved as a PNG in the session's home.</summary>
    public async Task<string> PictureAsync((string Name, string Path) item, CancellationToken cancel)
    {
        var script = Path.Combine(_session.Home, "pixmap.py");
        if (!File.Exists(script))
        {
            await File.WriteAllTextAsync(script, Pixmap, cancel).ConfigureAwait(false);
        }

        var picture = Path.Combine(_session.Home, $"icon-{Guid.NewGuid():N}.png");
        var saved = await _session.RunAsync("python3", [script, item.Name, item.Path, picture], cancel: cancel).ConfigureAwait(false);
        return saved.Exit == 0 ? picture : throw new InvalidOperationException($"The icon's pixmap was not read: {saved.Error}");
    }

    /// <summary>How many entries an icon's menu holds at its top, as the tray asks it through its menu's own interface.</summary>
    public async Task<int> MenuEntriesAsync((string Name, string Path) item, CancellationToken cancel)
    {
        // gdbus prints the path as (<objectpath '/MenuBar'>,).
        var menu = (await PropertyAsync(item, "Menu", cancel)).Trim('(', ')', '<', '>', ',', ' ')
            .Replace("objectpath ", "", StringComparison.Ordinal).Trim('\'');
        var layout = await _session.RunAsync(
            "gdbus", ["call", "--session", "--dest", item.Name, "--object-path", menu, "--method", "com.canonical.dbusmenu.GetLayout", "0", "1", "[]"],
            cancel: cancel).ConfigureAwait(false);
        // Each entry at the top is a child of the root's layout, a variant of its own; a separator between groups is none.
        var children = layout.Out.Split("<(", StringSplitOptions.None).Length - 1;
        return children - (layout.Out.Split("'type': <'separator'>", StringSplitOptions.None).Length - 1);
    }
}
