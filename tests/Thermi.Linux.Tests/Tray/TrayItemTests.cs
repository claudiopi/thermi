using System.Globalization;
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
            'no-such-object': ('/Nowhere', ITEM, 'Activate', GLib.Variant('(ii)', (0, 0))),
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

    // Opens the icon's menu as KDE Plasma's tray does, which keeps each row it made by its id and makes a row checkable only
    // as it first sees the row: says how many rows show a check, and keeps what it made in the file it is given.
    private const string Opener = """
        import sys, json, os, gi
        from gi.repository import Gio, GLib
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        made = json.load(open(sys.argv[3])) if os.path.exists(sys.argv[3]) else {}
        root = bus.call_sync(sys.argv[1], sys.argv[2], 'com.canonical.dbusmenu', 'GetLayout', GLib.Variant('(iias)', (0, -1, [])),
                             None, Gio.DBusCallFlags.NONE, 5000, None).unpack()[1]
        def rows(entry):
            yield entry
            for child in entry[2]:
                yield from rows(child)
        shown = 0
        for row_id, properties, _ in rows(root):
            checkable = made.setdefault(str(row_id), properties.get('toggle-type', '') != '')
            shown += checkable and properties.get('toggle-state', 0) == 1
        json.dump(made, open(sys.argv[3], 'w'))
        print(shown, flush=True)
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

    // A row checked since the tray first opened the menu shows its check the next time it opens, as on Windows, which builds
    // its menu each time: a tray that keeps what it made of each row, as Plasma's, would show the row as it first made it.
    [Fact(Timeout = Bound.Test)]
    public async Task ARowCheckedSinceTheMenuLastOpenedShowsItsCheck()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [new TestMonitor(1280, 720)], cancel);
        var session = desktop.Session;
        var tray = await TrayWatcher.ServeAsync(session, cancel);
        var overlay = Scene.Overlay with { Position = Scene.OnWindow };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay] }, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (name, _) = await tray.NextAsync(cancel);
        var script = Path.Combine(session.Home, "opener.py");
        await File.WriteAllTextAsync(script, Opener, cancel);
        string[] open = [script, name, "/MenuBar", Path.Combine(session.Home, "made.json")];
        var before = await session.RunAsync("python3", open, cancel: cancel);
        var edited = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay with { ClickThrough = true }] }, cancel);
        await edited;

        var after = await session.RunAsync("python3", open, cancel: cancel);

        Assert.Equal(int.Parse(before.Out, CultureInfo.InvariantCulture) + 1, int.Parse(after.Out, CultureInfo.InvariantCulture));
    }

    // A left click on thermi's icon does what it does on Windows: it opens the menu, which the icon says it is so a tray
    // opens it itself, but while the overlays are hidden, when it shows them, through the Activate the tray calls then.
    [Fact(Timeout = Bound.Test)]
    public async Task ALeftClickShowsTheOverlaysItHid()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [new TestMonitor(1280, 720)], cancel);
        var session = desktop.Session;
        var tray = await TrayWatcher.ServeAsync(session, cancel);
        var hotkeys = new HotkeysConfig { Toggle = "Ctrl+Shift+J" };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }], Hotkeys = hotkeys }, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var icon = await tray.NextAsync(cancel);
        var menuShown = await tray.PropertyAsync(icon, "ItemIsMenu", cancel);
        var hidden = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+j", cancel);
        await hidden;
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        var menuHidden = await tray.PropertyAsync(icon, "ItemIsMenu", cancel);

        var shown = Traced.NextAsync(thermi.Error, Traced.Shown, cancel);
        var activated = await session.RunAsync(
            "gdbus", ["call", "--session", "--dest", icon.Name, "--object-path", icon.Path, "--method", "org.kde.StatusNotifierItem.Activate", "0", "0"],
            cancel: cancel);
        await shown;

        Assert.Equal(0, activated.Exit);
        Assert.Equal(("(<true>,)", "(<false>,)"), (menuShown, menuHidden));
    }
}
