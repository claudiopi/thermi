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
    // it added to the Shell or its top bar. "hold" prints each signal of the extension's, a line each, and answers a right press with a
    // menu, as thermi does, saying once the Shell took it.
    private const string Caller = """
        import math, sys, gi
        from gi.repository import Gio, GLib
        OVERLAY, PATH, SHELL = 'io.github.claudiopi.thermi.Overlay', '/io/github/claudiopi/thermi', 'io.github.claudiopi.thermi.Shell'
        DESKTOP = 'io.github.claudiopi.thermi.Desktop'
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)

        def show(overlay, length=None, scale=1.0):
            pixels = bytes(40 * 4 * 20 if length is None else length)
            return 'Show', GLib.Variant('(uayiiiddda(dddd))', (overlay, pixels, 40, 20, 160, 100.0, 100.0, scale, [(0.0, 0.0, 40.0, 20.0)]))

        def menu(overlay, parent):
            return 'Menu', GLib.Variant('(udda(issbbb))', (overlay, 110.0, 110.0, [(parent, 'Row', '', False, False, False)]))

        def icons(count, length=None):
            pixels = bytes(16 * 4 * 16 if length is None else length)
            shown = [(f'icon{at}', '', pixels, 16, 16, 64, 1.0) for at in range(count)]
            return 'Icons', GLib.Variant('(a(ssayiiid))', (shown,))

        def hotkeys(count):
            return 'Hotkeys', GLib.Variant('(as)', (['<Control><Shift>F9'] * count,))

        def call(method, args, on=bus):
            try:
                interface = DESKTOP if method in ('Icons', 'Hotkeys') else SHELL
                on.call_sync('org.gnome.Shell', PATH, interface, method, args, None, Gio.DBusCallFlags.NONE, 5000, None)
                return 'taken'
            except GLib.Error:
                return 'refused'

        def actors():
            counted = 'Main.uiGroup.get_n_children() + Main.panel._rightBox.get_n_children()'
            said = bus.call_sync('org.gnome.Shell', '/org/gnome/Shell', 'org.gnome.Shell', 'Eval',
                                 GLib.Variant('(s)', (counted,)), None, Gio.DBusCallFlags.NONE, 5000, None)
            return int(said.unpack()[1])

        def another():
            address = Gio.dbus_address_get_for_bus_sync(Gio.BusType.SESSION, None)
            flags = Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT | Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION
            return Gio.DBusConnection.new_for_address_sync(address, flags, None, None)

        bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus', 'RequestName',
                      GLib.Variant('(su)', (OVERLAY, 4)), None, Gio.DBusCallFlags.NONE, -1, None)
        if sys.argv[1] == 'refuse':
            cases = {
                'short-pixels': ([], show(1, length=10), bus),
                'no-scale': ([], show(1, scale=0.0), bus),
                'nan-scale': ([], show(1, scale=math.nan), bus),
                'past-the-most-overlays': ([show(overlay) for overlay in range(64)], show(64), bus),
                'orphan-menu-row': ([show(1)], menu(1, 5), bus),
                'another-caller': ([], show(1), another()),
                'past-the-most-icons': ([], icons(65), bus),
                'short-icon-pixels': ([], icons(1, length=10), bus),
                'past-the-most-hotkeys': ([], hotkeys(9), bus),
            }
            first, last, on = cases[sys.argv[2]]
            taken = all(call(*each) == 'taken' for each in first)
            before = actors()
            got = call(*last, on=on)
            print(taken, got, actors() - before, flush=True)
        else:
            def heard(connection, sender, path, interface, signal, args):
                values = args.unpack()
                print(signal, *(values[1:5] if signal == 'Pointer' else values[1:]), flush=True)
                if signal == 'Pointer' and values[1] == 'press' and values[4] == 3:
                    print('menu', call(*menu(1, -1)), flush=True)
            bus.signal_subscribe(None, SHELL, None, PATH, None, Gio.DBusSignalFlags.NONE, heard)
            print('ready' if call(*show(1)) == 'taken' else 'refused', flush=True)
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
    [InlineData("another-caller")]
    [InlineData("past-the-most-icons")]
    [InlineData("short-icon-pixels")]
    [InlineData("past-the-most-hotkeys")]
    public async Task ACallOutsideTheProtocolIsRefusedAndAddsNothing(string row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);

        var said = await (await CallerAsync(desktop, ["refuse", row], cancel)).Out.ToEndAsync(cancel);

        Assert.Equal("True refused 0", said.Trim());
    }

    // A press of the first button holds the overlay and Esc, thermi's alone while it does; a right press opens thermi's
    // menu and holds nothing, so the pointer is the person's again once the menu closes.
    [Fact(Timeout = Bound.Test)]
    public async Task ThePointerAndTheKeysAreThermisOnlyWhileItsPressHolds()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var caller = await CallerAsync(desktop, ["hold"], cancel);
        var input = desktop.Input;
        var last = caller.Out.NextAsync(line => line.StartsWith("Pointer release 110", StringComparison.Ordinal), cancel, count: 3);

        await input.ClickAsync(OnOverlay.X, OnOverlay.Y, cancel);
        await input.PressAsync("a", cancel);
        await input.PressAsync("Escape", cancel);
        await input.HoldAsync(OnOverlay, OnOverlay, Keys, _ => Task.CompletedTask, cancel);
        await input.HoldAsync(OnOverlay, OnOverlay, Menu, _ => Task.CompletedTask, cancel, button: 3);
        await input.PressAsync("Escape", cancel);
        await input.ClickAsync(500, 500, cancel);
        await input.ClickAsync(OnOverlay.X, OnOverlay.Y, cancel);
        await last;

        var lines = caller.Out.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] keys = [.. lines.Where(line => line.StartsWith("Key", StringComparison.Ordinal))];
        Assert.Equal(["Key Escape"], keys);
        Assert.DoesNotContain(lines, line => line.StartsWith("Pointer press 500", StringComparison.Ordinal));

        async Task Keys(CancellationToken held)
        {
            await input.PressAsync("a", held);
            await input.PressAsync("Escape", held);
        }

        Task Menu(CancellationToken held)
        {
            return caller.Out.FirstAsync(line => line == "menu taken", held);
        }
    }

    private static async Task<SessionProcess> CallerAsync(TestDesktop desktop, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(desktop.Session.Home, "caller.py");
        await File.WriteAllTextAsync(script, Caller, cancel);
        var caller = await desktop.Session.StartAsync("python3", [script, .. args], cancel: cancel);
        if (args[0] == "hold")
        {
            Assert.Equal("ready", await caller.ReadyAsync(cancel));
        }

        return caller;
    }
}
