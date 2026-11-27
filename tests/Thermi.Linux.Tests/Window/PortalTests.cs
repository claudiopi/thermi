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

    // Owns the portal's name and answers ReadOne from its settings, the a{sv} its argument writes in GVariant's text; a line "<key> <type> <value>" on its input changes one,
    // as GVariant's text writes the value, and says so as the portal does.
    private const string Portal = """
        import sys, gi
        from gi.repository import Gio, GLib
        XML = '''<node><interface name="org.freedesktop.portal.Settings">
          <method name="ReadOne"><arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/></method>
          <signal name="SettingChanged"><arg type="s"/><arg type="s"/><arg type="v"/></signal>
        </interface></node>'''
        PATH, FACE, APPEARANCE = '/org/freedesktop/portal/desktop', 'org.freedesktop.portal.Settings', 'org.freedesktop.appearance'
        given = GLib.Variant.parse(GLib.VariantType.new('a{sv}'), sys.argv[1], None, None)
        entries = [given.get_child_value(at) for at in range(given.n_children())]
        settings = {entry.get_child_value(0).get_string(): entry.get_child_value(1).get_variant() for entry in entries}
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
        var seen = await WatchedAsync(
            "{'color-scheme': <uint32 0>, 'accent-color': <(-1.0, -1.0, -1.0)>, 'contrast': <uint32 0>}",
            ["color-scheme u 1", "accent-color (ddd) (0.2, 0.5, 1.0)", "contrast u 1", "color-scheme u 2"],
            TestContext.Current.CancellationToken);

        Assert.Equal(["none none False", "dark none False", "dark #3380ff False", "dark #3380ff True", "light #3380ff True"], seen);
    }

    // What the window cannot read says nothing, and never ends it: no portal at all; one that lacks a key, then changes
    // one into another shape; and one whose every value is of another shape than the portal's documented one.
    [Theory(Timeout = Bound.Test)] // Rows: no portal, a key missing, and values in other shapes.
    [InlineData(null, new string[0], "none none False")]
    [InlineData("{'color-scheme': <uint32 1>, 'contrast': <uint32 1>}", new[] { "accent-color s 'blue'", "contrast s 'yes'" }, "dark none True|dark none True|dark none False")]
    [InlineData("{'color-scheme': <'dark'>, 'accent-color': <uint32 3>, 'contrast': <'yes'>}", new string[0], "none none False")]
    public async Task WhatThePortalDoesNotSayIsNothing(string? settings, string[] changes, string seen) =>
        Assert.Equal(seen.Split('|'), await WatchedAsync(settings, changes, TestContext.Current.CancellationToken));

    // The window's lines as it starts and after each change, the portal of the test's own starting from settings, if any.
    private static async Task<List<string?>> WatchedAsync(string? settings, string[] changes, CancellationToken cancel)
    {
        await using var session = new OwnSession();
        var script = Path.Combine(session.Home, "portal.py");
        await File.WriteAllTextAsync(script, Portal, cancel);
        var portal = settings is null ? null : await session.StartAsync("python3", [script, settings], cancel: cancel);
        if (portal is not null)
        {
            await portal.ReadyAsync(cancel);
        }

        var window = await session.StartAsync(Built.Program("Thermi.Linux.Tests"), [Watch], cancel: cancel);
        List<string?> seen = [await window.Out.ReadLineAsync(cancel)];
        foreach (var change in changes)
        {
            await portal!.Input.WriteLineAsync(change.AsMemory(), cancel);
            await portal.Input.FlushAsync(cancel);
            seen.Add(await window.Out.ReadLineAsync(cancel));
        }

        window.Kill();
        portal?.Kill();
        return seen;
    }

    /// <summary>
    /// The tests' own program as <see cref="TheWindowFollowsTheDesktopsLookAsItChanges"/> starts it: the window's portal on a
    /// GLib loop of its own, a line of its scheme, accent and contrast as it starts and on each change.
    /// </summary>
    public static int WatchAsync()
    {
        Gio.Module.Initialize();
        // The session's one connection, which the window never closes.
        var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        var loop = GLib.MainLoop.New(null, false);
        DesktopPortal? portal = null;
        void Say()
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{portal!.Scheme switch { true => "dark", false => "light", null => "none" }} {portal.Accent ?? "none"} {portal.HighContrast}"));
        }

        using var watched = new DesktopPortal(bus, Say, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        portal = watched;
        Say();
        loop.Run();
        return 0;
    }
}
