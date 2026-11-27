using System.Globalization;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Window;

/// <summary>
/// The desktop's look as the settings window reads it, from the settings portal every desktop answers: as it is when the
/// window opens, and each time the person changes it. A portal of the test's own stands in for the desktop's.
/// </summary>
public sealed class PortalTests
{
    /// <summary>The tests' own program, started in a session with it, watches the portal as the window does (<see cref="WatchAsync"/>).</summary>
    public const string Watch = "--portal";

    // Owns the portal's name and answers ReadOne from its settings; a line "<key> <type> <value>" on its input changes one,
    // as GVariant's text writes the value, and says so as the portal does.
    private const string Portal = """
        import sys, gi
        from gi.repository import Gio, GLib
        XML = '''<node><interface name="org.freedesktop.portal.Settings">
          <method name="ReadOne"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method>
          <signal name="SettingChanged"><arg type="s"/><arg type="s"/><arg type="v"/></signal>
        </interface></node>'''
        PATH, FACE, APPEARANCE = '/org/freedesktop/portal/desktop', 'org.freedesktop.portal.Settings', 'org.freedesktop.appearance'
        settings = {'color-scheme': GLib.Variant('u', 0), 'accent-color': GLib.Variant('(ddd)', (-1.0, -1.0, -1.0)), 'contrast': GLib.Variant('u', 0)}
        state = {}
        def call(bus, sender, path, interface, method, args, invocation):
            space, key = args.unpack()
            if space == APPEARANCE and key in settings:
                invocation.return_value(GLib.Variant('(v)', (settings[key],)))
            else:
                invocation.return_dbus_error('org.freedesktop.portal.Error.NotFound', key)
        def bus_up(bus, name):
            state['bus'] = bus
            bus.register_object(PATH, Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call)
        def line(channel, condition):
            key, kind, value = sys.stdin.readline().split(' ', 2)
            settings[key] = GLib.Variant.parse(GLib.VariantType.new(kind), value.strip(), None, None)
            state['bus'].emit_signal(None, PATH, FACE, 'SettingChanged', GLib.Variant('(ssv)', (APPEARANCE, key, settings[key])))
            return True
        Gio.bus_own_name(Gio.BusType.SESSION, 'org.freedesktop.portal.Desktop', Gio.BusNameOwnerFlags.DO_NOT_QUEUE, bus_up,
                         lambda *_: print('ready', flush=True), lambda *_: sys.exit('The name is taken.'))
        GLib.io_add_watch(GLib.IOChannel.unix_new(0), GLib.IO_IN, line)
        GLib.MainLoop().run()
        """;

    // The window takes the desktop's scheme, its accent to the nearest of 255 steps a color, and its contrast, as they are
    // and each time they change, from the portal's own signal on GTK's loop: a desktop that prefers no scheme and has no
    // accent says none.
    [Fact(Timeout = Bound.Test)]
    public async Task TheWindowFollowsTheDesktopsLookAsItChanges()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var script = Path.Combine(session.Home, "portal.py");
        await File.WriteAllTextAsync(script, Portal, cancel);
        var portal = await session.StartAsync("python3", [script], cancel: cancel);
        await portal.ReadyAsync(cancel);
        var window = await session.StartAsync(Built.Program("Thermi.Linux.Tests"), [Watch], cancel: cancel);
        List<string?> seen = [await window.Out.ReadLineAsync(cancel)];

        foreach (var change in new[] { "color-scheme u 1", "accent-color (ddd) (0.2, 0.5, 1.0)", "contrast u 1", "color-scheme u 2" })
        {
            await portal.Input.WriteLineAsync(change.AsMemory(), cancel);
            await portal.Input.FlushAsync(cancel);
            seen.Add(await window.Out.ReadLineAsync(cancel));
        }

        window.Kill();
        portal.Kill();
        Assert.Equal(["none none False", "dark none False", "dark #3380ff False", "dark #3380ff True", "light #3380ff True"], seen);
    }

    /// <summary>
    /// The tests' own program as <see cref="TheWindowFollowsTheDesktopsLookAsItChanges"/> starts it: the window's portal on a
    /// GLib loop of its own, a line of its scheme, accent and contrast as it starts and on each change.
    /// </summary>
    public static int WatchAsync()
    {
        Gio.Module.Initialize();
        using var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        var loop = GLib.MainLoop.New(null, false);
        DesktopPortal? portal = null;
        void Say()
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{portal!.Scheme switch { true => "dark", false => "light", null => "none" }} {portal.Accent ?? "none"} {portal.HighContrast}"));
        }

        using var watched = new DesktopPortal(bus, Say);
        portal = watched;
        Say();
        loop.Run();
        return 0;
    }
}
