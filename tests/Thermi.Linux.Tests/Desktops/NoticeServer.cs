using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A notification server of the test's own, as the desktop's: it takes the session's <c>org.freedesktop.Notifications</c>,
/// tells the test each notice a program sends, and clicks a notice's button as a person does.
/// </summary>
internal sealed class NoticeServer
{
    // GLib from Python serves the freedesktop interface: each notice is a line of JSON, and a line "<id> <action>" on its
    // input invokes that button of that notice.
    private const string Code = """
        import json, sys, gi
        from gi.repository import Gio, GLib
        XML = '''<node><interface name="org.freedesktop.Notifications">
          <method name="Notify"><arg type="s" direction="in"/><arg type="u" direction="in"/><arg type="s" direction="in"/>
            <arg type="s" direction="in"/><arg type="s" direction="in"/><arg type="as" direction="in"/><arg type="a{sv}" direction="in"/>
            <arg type="i" direction="in"/><arg type="u" direction="out"/></method>
          <method name="CloseNotification"><arg type="u" direction="in"/></method>
          <method name="GetCapabilities"><arg type="as" direction="out"/></method>
          <method name="GetServerInformation"><arg type="s" direction="out"/><arg type="s" direction="out"/>
            <arg type="s" direction="out"/><arg type="s" direction="out"/></method>
          <signal name="ActionInvoked"><arg type="u"/><arg type="s"/></signal>
          <signal name="NotificationClosed"><arg type="u"/><arg type="u"/></signal>
        </interface></node>'''
        state = {'next': 0, 'bus': None}
        def call(bus, sender, path, interface, method, args, invocation):
            if method == 'Notify':
                state['next'] += 1
                app, _, icon, summary, body, actions, hints, _ = args.unpack()
                print(json.dumps({'id': state['next'], 'app': app, 'summary': summary, 'body': body, 'actions': actions[0::2]}), flush=True)
                invocation.return_value(GLib.Variant('(u)', (state['next'],)))
            elif method == 'GetCapabilities':
                invocation.return_value(GLib.Variant('(as)', (['actions', 'body'],)))
            elif method == 'GetServerInformation':
                invocation.return_value(GLib.Variant('(ssss)', ('thermi-tests', 'thermi', '1', '1.2')))
            else:
                invocation.return_value(None)
        def acquired(bus, name):
            print(json.dumps({'ready': True}), flush=True)
        def bus_up(bus, name):
            state['bus'] = bus
            bus.register_object('/org/freedesktop/Notifications', Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call)
        def line(channel, condition):
            notice, action = sys.stdin.readline().split()
            invoked = GLib.Variant('(us)', (int(notice), action))
            state['bus'].emit_signal(None, '/org/freedesktop/Notifications', 'org.freedesktop.Notifications', 'ActionInvoked', invoked)
            return True
        Gio.bus_own_name(Gio.BusType.SESSION, 'org.freedesktop.Notifications', 0, bus_up, acquired, None)
        GLib.io_add_watch(GLib.IOChannel.unix_new(0), GLib.IO_IN, line)
        GLib.MainLoop().run()
        """;

    private readonly Process _server;

    private NoticeServer(Process server)
    {
        _server = server;
    }

    /// <summary>Serves the session's notifications, and returns once the server holds their name.</summary>
    public static async Task<NoticeServer> ServeAsync(OwnSession session, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "notices.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var notices = new NoticeServer(await session.StartAsync("python3", [script], cancel: cancel).ConfigureAwait(false));
        return await notices.NextAsync(cancel).ConfigureAwait(false) is { } ready && ready["ready"] is not null
            ? notices
            : throw new InvalidOperationException("The notification server never took its name.");
    }

    /// <summary>The next notice a program sent: its id, program, summary, body and buttons' actions; null once the server ended.</summary>
    public async Task<JsonNode?> NextAsync(CancellationToken cancel) =>
        await _server.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false) is { } line ? JsonNode.Parse(line) : null;

    /// <summary>Clicks the button of <paramref name="notice"/> whose action is <paramref name="action"/>.</summary>
    public async Task ClickAsync(JsonNode notice, string action, CancellationToken cancel)
    {
        await _server.StandardInput.WriteLineAsync($"{notice["id"]} {action}".AsMemory(), cancel).ConfigureAwait(false);
        await _server.StandardInput.FlushAsync(cancel).ConfigureAwait(false);
    }
}
