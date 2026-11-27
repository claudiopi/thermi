using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>A notification a program sent: when, on the system's monotonic clock, its program, summary, and buttons by id and label.</summary>
internal sealed record SentNotice(double At, string App, string Summary, IReadOnlyList<string> Actions, IReadOnlyList<string> Labels, int Id);

/// <summary>
/// The session's notifications as the test sees them. On GNOME the Shell is the desktop's notification server: the test
/// watches the calls it gets, as <c>dbus-monitor</c> does with a match rule, and clicks a button through the Shell's own
/// message tray. On the other desktops a server of the test's own takes the name, and clicks a button as the person's
/// does. A notice is known by its buttons' action ids, never by its text.
/// </summary>
internal sealed class NoticeWatch
{
    // Prints each Notify call, each request to install an extension or look for their updates, and each of the test's syncs, as a line of JSON.
    private const string Monitor = """
        import json, sys, time, gi
        from gi.repository import Gio, GLib
        # A program's call names the server by its freedesktop name; GNOME's notifications service passes each on to the Shell
        # by the Shell's own, which is the same notice again.
        RULES = ["type='method_call',interface='org.freedesktop.Notifications',member='Notify',destination='org.freedesktop.Notifications'",
                 "type='method_call',interface='org.gnome.Shell.Extensions',member='InstallRemoteExtension'",
                 "type='method_call',interface='org.gnome.Shell.Extensions',member='CheckForUpdates'",
                 "type='signal',interface='org.thermi.Tests',member='Sync'"]
        # A filter sees what goes out as well as what comes in, and its None drops the message: the watcher's own call and
        # the bus's reply go on, and the calls watched stop here, as a monitor that answered one would be cut off by the bus.
        def seen(connection, message, incoming):
            if not incoming:
                return message
            interface, member, body = message.get_interface(), message.get_member(), message.get_body()
            said = None
            if interface == 'org.freedesktop.Notifications' and member == 'Notify':
                app, _, _, summary, text, actions, _, _ = body.unpack()
                said = {'app': app, 'summary': summary, 'actions': actions[0::2], 'labels': actions[1::2], 'id': 0}
            elif interface == 'org.gnome.Shell.Extensions' and member == 'InstallRemoteExtension':
                said = {'install': body.unpack()[0]}
            elif interface == 'org.gnome.Shell.Extensions' and member == 'CheckForUpdates':
                said = {'updates': True}
            elif interface == 'org.thermi.Tests' and member == 'Sync':
                said = {'sync': body.unpack()[0]}
            if said is not None:
                said['at'] = time.monotonic()
                print(json.dumps(said), flush=True)
            return None if message.get_message_type() == Gio.DBusMessageType.METHOD_CALL else message
        bus = Gio.DBusConnection.new_for_address_sync(
            sys.argv[1], Gio.DBusConnectionFlags.AUTHENTICATION_CLIENT | Gio.DBusConnectionFlags.MESSAGE_BUS_CONNECTION, None, None)
        bus.add_filter(seen)
        bus.call_sync('org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus.Monitoring', 'BecomeMonitor',
                      GLib.Variant('(asu)', (RULES, 0)), None, Gio.DBusCallFlags.NONE, -1, None)
        print(json.dumps({'ready': True}), flush=True)
        GLib.MainLoop().run()
        """;

    // Serves the freedesktop interface: each notice is a line of JSON, as the monitor's, and a line "<id> <action>" on its
    // input invokes that button of that notice.
    private const string Server = """
        import json, sys, time, gi
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
                app, _, _, summary, _, actions, _, _ = args.unpack()
                said = {'app': app, 'summary': summary, 'actions': actions[0::2], 'labels': actions[1::2], 'id': state['next'], 'at': time.monotonic()}
                print(json.dumps(said), flush=True)
                invocation.return_value(GLib.Variant('(u)', (state['next'],)))
            elif method == 'GetCapabilities':
                invocation.return_value(GLib.Variant('(as)', (['actions', 'body'],)))
            elif method == 'GetServerInformation':
                invocation.return_value(GLib.Variant('(ssss)', ('notices', 'tests', '1', '1.2')))
            else:
                invocation.return_value(None)
        def synced(bus, sender, path, interface, signal, args):
            print(json.dumps({'sync': args.unpack()[0], 'at': time.monotonic()}), flush=True)
        def bus_up(bus, name):
            state['bus'] = bus
            bus.register_object('/org/freedesktop/Notifications', Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call)
            bus.signal_subscribe(None, 'org.thermi.Tests', 'Sync', None, None, Gio.DBusSignalFlags.NONE, synced)
        def line(channel, condition):
            notice, action = sys.stdin.readline().split()
            invoked = GLib.Variant('(us)', (int(notice), action))
            state['bus'].emit_signal(None, '/org/freedesktop/Notifications', 'org.freedesktop.Notifications', 'ActionInvoked', invoked)
            return True
        Gio.bus_own_name(Gio.BusType.SESSION, 'org.freedesktop.Notifications', Gio.BusNameOwnerFlags.DO_NOT_QUEUE, bus_up,
                         lambda *_: print(json.dumps({'ready': True}), flush=True), lambda *_: sys.exit('The name is taken.'))
        GLib.io_add_watch(GLib.IOChannel.unix_new(0), GLib.IO_IN, line)
        GLib.MainLoop().run()
        """;

    private readonly TestDesktop _desktop;
    private readonly SessionProcess _watcher;

    private NoticeWatch(TestDesktop desktop, SessionProcess watcher)
    {
        _desktop = desktop;
        _watcher = watcher;
    }

    private bool Shell => _desktop.Kind is DesktopKind.Gnome or DesktopKind.GnomeX11;

    /// <summary>Watches the session's notifications, and returns once nothing sent from now on can pass unseen.</summary>
    public static async Task<NoticeWatch> StartAsync(TestDesktop desktop, CancellationToken cancel)
    {
        var shell = desktop.Kind is DesktopKind.Gnome or DesktopKind.GnomeX11;
        var script = Path.Combine(desktop.Session.Home, shell ? "notice-monitor.py" : "notice-server.py");
        await File.WriteAllTextAsync(script, shell ? Monitor : Server, cancel).ConfigureAwait(false);
        var watcher = await desktop.Session.StartAsync("python3", [script, desktop.Session.BusAddress], cancel: cancel).ConfigureAwait(false);
        return JsonNode.Parse(await watcher.ReadyAsync(cancel).ConfigureAwait(false))?["ready"] is not null
            ? new NoticeWatch(desktop, watcher)
            : throw new InvalidOperationException($"The notices are not watched: {watcher.Error.Text}");
    }

    /// <summary>The next notice sent from this call on with a button of <paramref name="action"/>.</summary>
    public Task<SentNotice> NextAsync(string action, CancellationToken cancel) => NextAsync(notice => notice.Actions.Contains(action), cancel);

    /// <summary>The next notice sent from this call on that <paramref name="which"/> takes.</summary>
    public async Task<SentNotice> NextAsync(Func<SentNotice, bool> which, CancellationToken cancel) =>
        Of(await _watcher.Out.NextAsync(line => Of(line) is { } notice && which(notice), cancel).ConfigureAwait(false))!;

    /// <summary>The next extension the Shell is asked to install from this call on, by its uuid.</summary>
    public async Task<string> NextInstallAsync(CancellationToken cancel) =>
        (string)JsonNode.Parse(await _watcher.Out.NextAsync(line => JsonNode.Parse(line)?["install"] is not null, cancel).ConfigureAwait(false))!["install"]!;

    /// <summary>The next time the Shell is asked to look for its extensions' updates, from this call on.</summary>
    public Task NextUpdateCheckAsync(CancellationToken cancel) =>
        _watcher.Out.NextAsync(line => JsonNode.Parse(line)?["updates"] is not null, cancel);

    /// <summary>How many notices with a button of <paramref name="action"/> were sent so far.</summary>
    public int Count(string action) => _watcher.Out.Text.Split('\n').Count(line => Of(line)?.Actions.Contains(action) == true);

    /// <summary>
    /// Returns once the watcher saw every notification the bus got before this call: a signal of the test's own, which the
    /// bus hands on after them.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancel)
    {
        var token = Guid.NewGuid().ToString("N");
        var synced = _watcher.Out.NextAsync(line => (string?)JsonNode.Parse(line)?["sync"] == token, cancel);
        var sent = await _desktop.Session.RunAsync(
            "gdbus", ["emit", "--session", "--object-path", "/org/thermi/Tests", "--signal", "org.thermi.Tests.Sync", $"'{token}'"], cancel: cancel)
            .ConfigureAwait(false);
        _ = sent.Exit == 0 ? 0 : throw new InvalidOperationException($"No sync: {sent.Error}");
        await synced.ConfigureAwait(false);
    }

    /// <summary>Clicks the button of <paramref name="notice"/> whose action is <paramref name="action"/>, as a person does.</summary>
    public async Task ClickAsync(SentNotice notice, string action, CancellationToken cancel)
    {
        if (!Shell)
        {
            await _watcher.Input.WriteLineAsync($"{notice.Id} {action}".AsMemory(), cancel).ConfigureAwait(false);
            await _watcher.Input.FlushAsync(cancel).ConfigureAwait(false);
            return;
        }

        // The Shell's message tray holds each notification with its buttons by label, as it shows them. It adds one only
        // after it answered the call the watcher saw, so the button is pressed once the notification is there.
        var label = notice.Labels[notice.Actions.ToList().IndexOf(action)];
        await _desktop.Input.ShellAsync(
            $"const title = {Quoted(notice.Summary)}; const label = {Quoted(label)};"
                + " const press = notice => { const button = (notice.actions ?? notice._actions).find(each => each.label === label);"
                + " (button.activate ?? button.callback).call(button); };"
                + " const there = Main.messageTray.getSources().flatMap(source => source.notifications).findLast(each => each.title === title);"
                + " if (there) { press(there); } else { const held = [];"
                + " const watch = source => held.push([source, source.connect('notification-added', (_, added) => {"
                + " if (added.title !== title) return; held.forEach(([object, id]) => object.disconnect(id)); press(added); })]);"
                + " Main.messageTray.getSources().forEach(watch);"
                + " held.push([Main.messageTray, Main.messageTray.connect('source-added', (_, source) => watch(source))]); }",
            cancel).ConfigureAwait(false);
    }

    private static string Quoted(string text) => JsonSerializer.Serialize(text);

    private static SentNotice? Of(string line) => JsonNode.Parse(line) is { } said && said["actions"] is JsonArray actions
        ? new SentNotice(
            (double)said["at"]!,
            (string)said["app"]!,
            (string)said["summary"]!,
            [.. actions.Select(action => (string)action!)],
            [.. said["labels"]!.AsArray().Select(label => (string)label!)],
            (int)said["id"]!)
        : null;
}
