using Thermi.Core.Settings;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Tray;

/// <summary>
/// thermi's tray icons answer whoever calls them on the session's bus, a tray or any other program, so a call of no shape
/// theirs is refused with D-Bus' error, and the icon answers the next call as before.
/// </summary>
public sealed class TrayItemTests
{
    // Makes the case's call to the item at the bus name it is given, then asks the item its id: says what the first got,
    // and the id.
    private const string Caller = """
        import sys, gi
        from gi.repository import Gio, GLib
        ITEM, MENU, PROPERTIES = 'org.kde.StatusNotifierItem', 'com.canonical.dbusmenu', 'org.freedesktop.DBus.Properties'
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        cases = {
            'wrong-arguments': ('/StatusNotifierItem', ITEM, 'Activate', GLib.Variant('(s)', ('x',))),
            'no-such-method': ('/StatusNotifierItem', ITEM, 'Explode', None),
            'no-such-interface': ('/StatusNotifierItem', 'org.example.Nothing', 'Activate', GLib.Variant('(ii)', (0, 0))),
            'no-such-property': ('/StatusNotifierItem', PROPERTIES, 'Get', GLib.Variant('(ss)', (ITEM, 'Nothing'))),
            'no-such-object': ('/Nowhere', 'org.freedesktop.DBus.Introspectable', 'Introspect', None),
            'no-such-menu-row': ('/MenuBar', MENU, 'GetLayout', GLib.Variant('(iias)', (999, -1, []))),
            'event-on-no-row': ('/MenuBar', MENU, 'Event', GLib.Variant('(isvu)', (999, 'clicked', GLib.Variant('i', 0), 0))),
        }
        path, interface, method, args = cases[sys.argv[2]]
        try:
            bus.call_sync(sys.argv[1], path, interface, method, args, None, Gio.DBusCallFlags.NONE, 5000, None)
            got = 'taken'
        except GLib.Error:
            got = 'refused'
        said = bus.call_sync(sys.argv[1], '/StatusNotifierItem', PROPERTIES, 'Get', GLib.Variant('(ss)', (ITEM, 'Id')),
                             None, Gio.DBusCallFlags.NONE, 5000, None)
        print(got, said.unpack()[0], flush=True)
        """;

    [Theory(Timeout = Bound.Test)]
    [InlineData("wrong-arguments")]
    [InlineData("no-such-method")]
    [InlineData("no-such-interface")]
    [InlineData("no-such-property")]
    [InlineData("no-such-object")]
    [InlineData("no-such-menu-row")]
    [InlineData("event-on-no-row")]
    public async Task ACallOfNoShapeOfTheIconsIsRefused(string row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [new TestMonitor(1280, 720)], cancel);
        var session = desktop.Session;
        var tray = await TrayWatcher.ServeAsync(session, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        await desktop.ShowThermiAsync([], cancel);
        var (name, _) = await tray.NextAsync(cancel);
        var script = Path.Combine(session.Home, "caller.py");
        await File.WriteAllTextAsync(script, Caller, cancel);

        var called = await session.RunAsync("python3", [script, name, row], cancel: cancel);

        Assert.Equal("refused thermi", called.Out.Trim());
    }
}
