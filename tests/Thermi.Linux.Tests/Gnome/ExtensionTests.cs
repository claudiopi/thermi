using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Gnome;

/// <summary>
/// thermi's GNOME Shell extension runs in the person's whole session, so it takes from its bus only what thermi's protocol
/// asks, and only from thermi, and holds the pointer and the keys no longer than a press of thermi's. A program of the
/// test's own stands in for thermi, owning its name, so each call and each press is the test's to choose.
/// </summary>
public sealed class ExtensionTests
{
    // Owns thermi's name and shows an overlay of 40 by 20 at (100, 100), all of it a box that takes the pointer. "refuse"
    // makes a case's calls, then its last, and says whether the first were taken, what the last got, and how many actors
    // it added to the Shell or its top bar. "shows" makes a case's calls and says what the Shell shows of them, read through
    // its Eval once it painted a frame. "hold" and "icons" print the key focus, then each signal of the extension's, a line
    // each: "hold" shows the overlay, prints the key focus again at each press of the first button, and answers a right
    // press with a menu, as thermi does, saying once the Shell took it; "icons" shows an icon and puts the top bar's focus
    // on it.
    private const string Caller = """
        import json, math, sys, gi
        from gi.repository import Gio, GLib
        OVERLAY, PATH, SHELL = 'io.github.claudiopi.thermi.Overlay', '/io/github/claudiopi/thermi', 'io.github.claudiopi.thermi.Shell'
        DESKTOP = 'io.github.claudiopi.thermi.Desktop'
        PNG = bytes([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]) + b'thermi'
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)

        def show(overlay, length=None, scale=1.0):
            pixels = bytes(40 * 4 * 20 if length is None else length)
            return 'Show', GLib.Variant('(uayiiiddda(dddd))', (overlay, pixels, 40, 20, 160, 100.0, 100.0, scale, [(0.0, 0.0, 40.0, 20.0)]))

        def row(text, parent=-1, checked=False, default=False, radio=False):
            return parent, text, '', checked, False, False, default, radio

        def menu(overlay, parent, side='below', at=(110.0, 110.0, 0.0, 0.0), rows=None):
            rows = rows or [row('Row', parent)]
            return 'Menu', GLib.Variant('(uddddsa(issbbbbb))', (overlay, *at, side, rows))

        def icons_of(keys, length=None, kept=False):
            pixels = b'' if kept else bytes(16 * 4 * 16 if length is None else length)
            side, stride = (0, 0) if kept else (16, 64)
            return 'Icons', GLib.Variant('(a(ssayiiid))', ([(key, '', pixels, side, side, stride, 1.0) for key in keys],))

        def icons(count, length=None):
            return icons_of([f'icon{at}' for at in range(count)], length)

        def hotkeys(count):
            return 'Hotkeys', GLib.Variant('(as)', (['<Control><Shift>F9'] * count,))

        def call(method, args, on=bus):
            try:
                interface = DESKTOP if method in ('Icons', 'IconMenu', 'Hotkeys') else SHELL
                on.call_sync('org.gnome.Shell', PATH, interface, method, args, None, Gio.DBusCallFlags.NONE, 5000, None)
                return 'taken'
            except GLib.Error:
                return 'refused'

        def evaluate(code):
            said = bus.call_sync('org.gnome.Shell', '/org/gnome/Shell', 'org.gnome.Shell', 'Eval',
                                 GLib.Variant('(s)', (code,)), None, Gio.DBusCallFlags.NONE, 5000, None)
            return json.loads(said.unpack()[1] or 'null')

        def frame():
            bus.call_sync('org.thermi.TestShell', '/org/thermi/TestShell', 'org.thermi.TestShell', 'Frame', None, None,
                          Gio.DBusCallFlags.NONE, 5000, None)

        def actors():
            return evaluate('Main.uiGroup.get_n_children() + Main.panel._rightBox.get_n_children()')

        def another():
            address = Gio.dbus_address_get_for_bus_sync(Gio.BusType.SESSION, None)
            flags = Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT | Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION
            return Gio.DBusConnection.new_for_address_sync(address, flags, None, None)

        BOX = 'const C = imports.gi.Clutter; const box = global.stage.get_actor_at_pos(C.PickMode.REACTIVE, 110, 110);'
        MENU = '''const open = Main.uiGroup.get_children().find(actor => actor.visible && actor.has_style_class_name?.('popup-menu-boxpointer'));
            const [, top] = open.get_transformed_position(); const [, height] = open.get_transformed_size();
            const label = (actor, text) => actor.text === text ? actor : actor.get_children().map(child => label(child, text)).find(found => found);
            const weight = text => label(open, text).get_theme_node().get_font().get_weight();
            `${top >= 519 ? 'below' : top + height <= 501 ? 'above' : 'over'} ${weight('Default') > weight('Other') ? 'bold' : 'plain'}`'''
        MARKS = '''const open = Main.uiGroup.get_children().find(actor => actor.visible && actor.has_style_class_name?.('popup-menu-boxpointer'));
            const label = (actor, text) => actor.text === text ? actor : actor.get_children().map(child => label(child, text)).find(found => found);
            label(open, TEXT).get_parent()._ornamentIcon.icon_name || 'none' '''
        COPY = '''const St = imports.gi.St; const Gio = imports.gi.Gio;
            St.Clipboard.get_default().get_content(St.ClipboardType.CLIPBOARD, 'image/png', (clipboard, bytes) => {
                global.thermiCopied = bytes ? [...bytes.get_data()] : [];
                Gio.bus_own_name(Gio.BusType.SESSION, 'org.thermi.Copied', Gio.BusNameOwnerFlags.NONE, null, null, null);
            });'''

        def shapes():
            said = []
            for shape in ('nwse-resize', 'not-allowed', 'default'):
                call('Cursor', GLib.Variant('(us)', (1, shape)))
                said.append(evaluate(BOX + 'Object.keys(C.CursorType).find(name => C.CursorType[name] === box.get_cursor_type())'))
            return ' '.join(said)

        def opened(side):
            call(*menu(1, -1, side, (110.0, 500.0, 1.0, 20.0), [row('Default', default=True), row('Other')]))
            frame()
            return evaluate(MENU)

        def marked(text):
            call(*menu(1, -1, rows=[row('Chosen', checked=True, radio=True), row('Not chosen', radio=True), row('Checked', checked=True)]))
            frame()
            return evaluate(MARKS.replace('TEXT', json.dumps(text)))

        def copied():
            call('Copy', GLib.Variant('(ay)', (PNG,)))
            loop = GLib.MainLoop()
            bus.signal_subscribe('org.freedesktop.DBus', 'org.freedesktop.DBus', 'NameOwnerChanged', '/org/freedesktop/DBus',
                                 'org.thermi.Copied', Gio.DBusSignalFlags.NONE, lambda *_: loop.quit())
            evaluate(COPY)
            loop.run()
            return 'image/png ' + ('same' if bytes(evaluate('global.thermiCopied')) == PNG else 'other')

        def full_screen():
            return evaluate(BOX + "box?.thermiAt ? 'shown' : 'hidden'")

        def icon_after_its_menu():
            call(*icons(1))
            call('IconMenu', GLib.Variant('(sa(issbbbbb))', ('icon0', [row('Row')])))
            frame()
            evaluate("Main.panel.statusArea['thermi-icon0'].menu.close(); 0")
            frame()
            return evaluate("Main.panel.statusArea['thermi-icon0'].visible ? 'shown' : 'hidden'")

        bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus', 'RequestName',
                      GLib.Variant('(su)', (OVERLAY, 4)), None, Gio.DBusCallFlags.NONE, -1, None)
        if sys.argv[1] == 'refuse':
            cases = {
                'short-pixels': ([], show(1, length=10), bus),
                'no-scale': ([], show(1, scale=0.0), bus),
                'nan-scale': ([], show(1, scale=math.nan), bus),
                'past-the-most-overlays': ([show(overlay) for overlay in range(64)], show(64), bus),
                'orphan-menu-row': ([show(1)], menu(1, 5), bus),
                'a-menu-on-no-side': ([show(1)], menu(1, -1, side='left'), bus),
                'a-pointer-of-no-shape': ([show(1)], ('Cursor', GLib.Variant('(us)', (1, 'wait'))), bus),
                'a-copy-of-no-png': ([], ('Copy', GLib.Variant('(ay)', (PNG[1:],))), bus),
                'another-caller': ([], show(1), another()),
                'past-the-most-icons': ([], icons(65), bus),
                'past-the-most-hotkeys': ([], hotkeys(9), bus),
                'short-icon-pixels': ([], icons(1, length=10), bus),
                'a-sensors-path': ([], icons_of(['item:/lpc/it8689e/0/fan/0']), bus),
                'an-empty-key-beside-another': ([], icons_of(['', 'item:/lpc/it8689e/0/fan/0']), bus),
                'a-new-icon-with-no-picture': ([], icons_of(['icon0'], kept=True), bus),
                'an-icon-kept-as-another-goes': ([icons_of(['icon0', 'icon1'])], icons_of(['icon1'], kept=True), bus),
            }
            first, last, on = cases[sys.argv[2]]
            taken = all(call(*each) == 'taken' for each in first)
            before = actors()
            got = call(*last, on=on)
            print(taken, got, actors() - before, flush=True)
        elif sys.argv[1] == 'shows':
            call(*show(1))
            frame()
            rows = {'shapes': shapes, 'menu-below': lambda: opened('below'), 'menu-above': lambda: opened('above'),
                    'radio-chosen': lambda: marked('Chosen'), 'radio-unchosen': lambda: marked('Not chosen'), 'copy': copied,
                    'full-screen': full_screen, 'icon-menu': icon_after_its_menu}
            print(rows[sys.argv[2]](), flush=True)
        else:
            focus = lambda: print('focus', evaluate('String(global.stage.key_focus)'), flush=True)
            pressed = {}
            def heard(connection, sender, path, interface, signal, args):
                values = args.unpack()
                print(signal, *(values[1:5] if signal == 'Pointer' else values[1:]), flush=True)
                if signal == 'Pointer' and values[1] == 'press':
                    pressed['at'] = values[6]
                if signal == 'Pointer' and values[1] == 'cancel':
                    print('cancel', 'after its press' if 0 < pressed['at'] <= values[6] else f'at {values[6]}', flush=True)
                if signal == 'Pointer' and values[1] == 'press' and values[4] == 1:
                    focus()
                if signal == 'Pointer' and values[1] == 'press' and values[4] == 3:
                    print('menu', call(*menu(1, -1)), flush=True)
            bus.signal_subscribe(None, None, None, PATH, None, Gio.DBusSignalFlags.NONE, heard)
            if sys.argv[1] == 'hold':
                shown = call(*show(1)) == 'taken'
            else:
                shown = call(*icons(1)) == 'taken'
                frame()
                shown = shown and evaluate("Main.panel.statusArea['thermi-icon0'].grab_key_focus(); 'focused'") == 'focused'
            focus()
            print('ready' if shown else 'refused', flush=True)
            GLib.MainLoop().run()
        """;

    private static readonly TestMonitor Monitor = new(1920, 1080);

    private static readonly (int X, int Y) OnOverlay = (110, 110);

    // Rows: each a bound of its own, which a call past it shows nothing of.
    [Theory(Timeout = Bound.Test)]
    [InlineData("short-pixels")]
    [InlineData("no-scale")]
    [InlineData("nan-scale")]
    [InlineData("past-the-most-overlays")]
    [InlineData("orphan-menu-row")]
    [InlineData("a-menu-on-no-side")]
    [InlineData("a-pointer-of-no-shape")]
    [InlineData("a-copy-of-no-png")]
    [InlineData("another-caller")]
    [InlineData("past-the-most-icons")]
    [InlineData("past-the-most-hotkeys")]
    public async Task ACallOutsideTheProtocolIsRefusedAndAddsNothing(string row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);

        var said = await (await CallerAsync(desktop, ["refuse", row], cancel)).Out.ToEndAsync(cancel);

        Assert.Equal("True refused 0", said.Trim());
    }

    // An icon's key is any string up to a bound, as an item's id after its prefix, a sensor's path among them; an icon out
    // of the protocol is left out of the call, which shows the others; one thermi did not redraw keeps its picture, and a
    // new one has none to keep. Says how many actors the call added to the top bar.
    [Theory(Timeout = Bound.Test)]
    [InlineData("short-icon-pixels", 0)]
    [InlineData("a-sensors-path", 1)]
    [InlineData("an-empty-key-beside-another", 1)]
    [InlineData("a-new-icon-with-no-picture", 0)]
    [InlineData("an-icon-kept-as-another-goes", -1)]
    public async Task AnIconOutsideTheProtocolIsLeftOutAndTheOthersShow(string row, int added)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);

        var said = await (await CallerAsync(desktop, ["refuse", row], cancel)).Out.ToEndAsync(cancel);

        Assert.Equal($"True taken {added}", said.Trim());
    }

    // What thermi asks shows as the Shell's own: the pointer's shapes over the overlay, back to the Shell's after; a menu
    // against a box, on the side thermi says, its default row bold; a picture on the clipboard; and the overlay over a
    // window full screen, where thermi, not the Shell, says whether it shows.
    // Rows: each a call of its own, the full-screen window the Shell would hide the overlay over, and an icon's menu, which
    // leaves its icon on the top bar once it closes.
    [Theory(Timeout = Bound.Test)]
    [InlineData("shapes", "NWSE_RESIZE NOT_ALLOWED INHERIT")]
    [InlineData("menu-below", "below bold")]
    [InlineData("menu-above", "above bold")]
    [InlineData("copy", "image/png same")]
    [InlineData("radio-chosen", "ornament-dot-checked-symbolic")]
    [InlineData("radio-unchosen", "ornament-dot-unchecked-symbolic")]
    [InlineData("full-screen", "shown")]
    [InlineData("icon-menu", "shown")]
    public async Task WhatThermiAsksShowsAsTheShellsOwn(string row, string shown)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        if (row == "full-screen")
        {
            await desktop.OpenWindowAsync(fullScreen: true, cancel);
        }

        var said = await (await CallerAsync(desktop, ["shows", row], cancel)).Out.ToEndAsync(cancel);

        Assert.Equal(shown, said.Trim());
    }

    // A press of the first button holds the overlay and Esc, thermi's alone while it does, and leaves the key focus where
    // it was; a right press opens thermi's menu and holds nothing, so the pointer is the person's again once the menu
    // closes; and another grab that takes the pointer from a press, as a modal dialog's, ends it as Esc does. The overview
    // does not open while a grab holds, so it never takes one.
    [Fact(Timeout = Bound.Test)]
    public async Task ThePointerAndTheKeysAreThermisOnlyWhileItsPressHolds()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var caller = await CallerAsync(desktop, ["hold"], cancel);
        var input = desktop.Input;
        var last = caller.Out.NextAsync(line => line.StartsWith("Pointer release 110", StringComparison.Ordinal), cancel, count: 3);
        var holding = caller.Out.NextAsync(line => line.StartsWith("focus", StringComparison.Ordinal), cancel, count: 2);

        await input.ClickAsync(OnOverlay.X, OnOverlay.Y, cancel);
        await input.PressAsync("a", cancel);
        await input.PressAsync("Escape", cancel);
        await input.HoldAsync(OnOverlay, OnOverlay, Keys, _ => Task.CompletedTask, cancel);
        await input.HoldAsync(OnOverlay, OnOverlay, Menu, _ => Task.CompletedTask, cancel, button: 3);
        await input.PressAsync("Escape", cancel);
        await input.ClickAsync(500, 500, cancel);
        await input.ClickAsync(OnOverlay.X, OnOverlay.Y, cancel);
        await last;
        var cancelled = caller.Out.NextAsync(line => line.StartsWith("cancel", StringComparison.Ordinal), cancel);
        await input.HoldAsync(OnOverlay, OnOverlay, Lose, there => input.ShellAsync("Main.popModal(global.thermiModal)", there), cancel);

        var lines = caller.Out.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] keys = [.. lines.Where(line => line.StartsWith("Key", StringComparison.Ordinal))];
        Assert.Equal(["Key Escape"], keys);
        Assert.DoesNotContain(lines, line => line.StartsWith("Pointer press 500", StringComparison.Ordinal));
        Assert.Single(lines.Where(line => line.StartsWith("focus", StringComparison.Ordinal)).Distinct());
        Assert.Contains("cancel after its press", lines);

        async Task Keys(CancellationToken held)
        {
            await holding.WaitAsync(held);
            await input.PressAsync("a", held);
            await input.PressAsync("Escape", held);
        }

        Task Menu(CancellationToken held)
        {
            return caller.Out.FirstAsync(line => line == "menu taken", held);
        }

        async Task Lose(CancellationToken held)
        {
            await input.ShellAsync("global.thermiModal = Main.pushModal(global.stage); 0", held);
            await cancelled.WaitAsync(held);
        }
    }

    // An icon the top bar's focus is on takes Enter, Space and the Menu key, as thermi takes a click and a right click; any
    // other key moves the focus, as on the Shell's own icons.
    [Fact(Timeout = Bound.Test)]
    public async Task AnIconTakesEnterSpaceAndTheMenuKey()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var caller = await CallerAsync(desktop, ["icons"], cancel);
        var menu = caller.Out.NextAsync(line => line == "IconKey Menu", cancel);

        foreach (var key in new[] { "a", "Return", "space", "Menu" })
        {
            await desktop.Input.PressAsync(key, cancel);
        }

        await menu;
        var lines = caller.Out.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] keys = [.. lines.Where(line => line.StartsWith("IconKey", StringComparison.Ordinal))];
        Assert.Equal(["IconKey Return", "IconKey space", "IconKey Menu"], keys);
    }

    private static async Task<SessionProcess> CallerAsync(TestDesktop desktop, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(desktop.Session.Home, "caller.py");
        await File.WriteAllTextAsync(script, Caller, cancel);
        var caller = await desktop.Session.StartAsync("python3", [script, .. args], cancel: cancel);
        if (args[0] is "hold" or "icons")
        {
            Assert.Equal("ready", await caller.Out.FirstAsync(line => line is "ready" or "refused", cancel));
        }

        return caller;
    }
}
