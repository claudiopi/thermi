using Microsoft.Extensions.Logging;
using Thermi.Core;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Hotkeys;

/// <summary>
/// thermi's hotkeys through the desktop's GlobalShortcuts portal, as KDE Plasma and Hyprland offer it: thermi asks for each
/// with its combination as the one it prefers, the desktop asks the person once, and says which it bound. Taking them
/// again closes the session the last ones were bound in and binds the new ones in a session of their own. A desktop with
/// no such portal, as Sway, or a person who said no or never answered, refuses them all (<see cref="Refused"/>). It asks on
/// a connection of its own: the portal takes a program's id only before its first call, and GTK's own calls on the shared
/// one come first. The portal answers on GDBus' thread, so each answer is taken back to the main loop's before thermi reads it.
/// </summary>
internal sealed class PortalHotkeys : IHotkeyGrab
{
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string Desktop = "/org/freedesktop/portal/desktop";
    private const string Shortcuts = "org.freedesktop.portal.GlobalShortcuts";
    private const string Request = "org.freedesktop.portal.Request";

    // thermi's id, its launcher's name, by which a desktop keeps what the person allowed it across starts.
    private const string AppId = "io.github.claudiopi.thermi";

    private static readonly WindowText[] Descriptions =
        [WindowText.HotkeyToggle, WindowText.HotkeyNextLook, WindowText.HotkeyClickThrough, WindowText.HotkeyResetMinMax];

    private readonly Gio.DBusConnection _bus;
    private readonly Func<Texts> _texts;
    private readonly ILogger _log;
    private Gio.DBusProxy? _shortcuts;
    private string? _session;
    private int _tokens;
    private bool _registered;

    public PortalHotkeys(Func<Texts> texts, ILogger log)
    {
        (_texts, _log) = (texts, log);
        var address = Gio.Functions.DbusAddressGetForBusSync(Gio.BusType.Session, null);
        _bus = Gio.DBusConnection.NewForAddressSync(
            address, Gio.DBusConnectionFlags.AuthenticationClient | Gio.DBusConnectionFlags.MessageBusConnection, null, null);
    }

    public Action<int> Pressed { get; set; } = _ => { };

    public Action Moved { get; set; } = () => { };

    public bool AsksThePerson => true;

    public bool Refused { get; private set; }

    public Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys) => Take(hotkeys, CancellationToken.None);

    public async Task<IReadOnlyList<bool>> Take(IReadOnlyList<Hotkey?> hotkeys, CancellationToken cancel)
    {
        await RegisterAsync();
        if (_session is { } last)
        {
            _session = null;
            await CloseAsync(last);
        }

        Refused = false;

        var wanted = hotkeys.Select((hotkey, at) => (Role: KeyNames.RoleOf(Core.Hotkeys.Roles[at]), Hotkey: hotkey, Text: Descriptions[at])).ToList();
        if (wanted.All(each => each.Hotkey is null))
        {
            return [.. hotkeys.Select(_ => true)];
        }

        var created = await RequestAsync(
            "CreateSession", token => [Options(("session_handle_token", GLib.Variant.NewString(token)), Handle(token))], cancel);
        if (created?.LookupValue("session_handle", null)?.GetString(out _) is not { } session)
        {
            Refused = true;
            return [.. hotkeys.Select(hotkey => hotkey is null)];
        }

        var texts = _texts();
        var asked = wanted.Where(each => each.Hotkey is not null).Select(each => GLib.Variant.NewTuple(
        [
            GLib.Variant.NewString(each.Role),
            Options(
                ("description", GLib.Variant.NewString(texts.Of(each.Text))),
                ("preferred_trigger", GLib.Variant.NewString(KeyNames.Trigger(each.Hotkey!.Value)))),
        ]));
        var shortcuts = GLib.Variant.NewArray(GLib.VariantType.New("(sa{sv})"), [.. asked]);
        _session = session;
        var bound = await RequestAsync(
            "BindShortcuts", token => [GLib.Variant.NewObjectPath(session), shortcuts, GLib.Variant.NewString(""), Options(Handle(token))], cancel);
        Refused = bound is null;
        var ids = bound?.LookupValue("shortcuts", GLib.VariantType.New("a(sa{sv})")) is { } taken
            ? Enumerable.Range(0, (int)taken.NChildren()).Select(at => taken.GetChildValue((nuint)at).GetChildValue(0).GetString(out _)).ToHashSet()
            : [];
        return [.. wanted.Select(each => each.Hotkey is null || ids.Contains(each.Role))];
    }

    // The portal ends the sessions of a connection that closes.
    public void Dispose()
    {
        if (_shortcuts is not null)
        {
            MainLoopRelease.Hold(_log, _shortcuts);
            _shortcuts.Dispose();
        }

        try
        {
            _bus.CloseSync(null);
        }
        catch (GLib.GException)
        {
            // The bus went first.
        }

        _bus.Dispose();
    }

    private async Task CloseAsync(string path, string face = "org.freedesktop.portal.Session")
    {
        try
        {
            await _bus.CallAsync(Portal, path, face, "Close");
        }
        catch (GLib.GException)
        {
            // The portal closed it already, as it does when the person revokes it.
        }

        await Callbacks.OnMainLoop();
    }

    // A program outside a sandbox says who it is before its first call, which a portal older than the registry ignores; the
    // call starts the portal, whose presses thermi then hears.
    private async Task RegisterAsync()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        try
        {
            var registry = GLib.Variant.NewTuple([GLib.Variant.NewString(AppId), Options()]);
            await _bus.CallAsync(Portal, Desktop, "org.freedesktop.host.portal.Registry", "Register", registry);
        }
        catch (GLib.GException)
        {
            // Older portals know thermi by its process instead.
        }

        await Callbacks.OnMainLoop();
        _shortcuts = Listen(Desktop, Shortcuts);
        _shortcuts.OnGSignal += (_, heard) =>
            Callbacks.Safely(() => Activated(heard.SignalName, heard.Parameters), ex => _log.SignalFailed(heard.SignalName, ex));
    }

    // A portal's call answers with a request, whose Response comes later at a path thermi's token names: listened to before
    // the call, so it cannot come first. The results, or null where the person said no, or never answered before
    // <paramref name="cancel"/>, as the request is closed then, or the desktop has no such portal.
    private async Task<GLib.Variant?> RequestAsync(string method, Func<string, GLib.Variant[]> args, CancellationToken cancel)
    {
        var token = $"thermi{++_tokens}";
        var sender = _bus.GetUniqueName()!.TrimStart(':').Replace('.', '_');
        var path = $"{Desktop}/request/{sender}/{token}";
        var answered = new TaskCompletionSource<GLib.Variant>(TaskCreationOptions.RunContinuationsAsynchronously);
        Gio.DBusProxy? request = null;
        try
        {
            request = Listen(path, Request);
            request.OnGSignal += (_, heard) =>
            {
                if (heard.SignalName == "Response")
                {
                    answered.TrySetResult(heard.Parameters);
                }
            };
            await _bus.CallAsync(Portal, Desktop, Shortcuts, method, GLib.Variant.NewTuple(args(token)));
            var response = await answered.Task.WaitAsync(cancel);
            return response.GetTypeString() == "(ua{sv})" && response.GetChildValue(0).GetUint32() == 0 ? response.GetChildValue(1) : null;
        }
        catch (GLib.GException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            await Callbacks.OnMainLoop();
            await CloseAsync(path, Request);
            return null;
        }
        finally
        {
            // Every way out, a fault no catch takes among them, comes back to the main loop, where the request is let go.
            await Callbacks.OnMainLoop();
            if (request is not null)
            {
                MainLoopRelease.Hold(_log, request);
                request.Dispose();
            }
        }
    }

    private void Activated(string signal, GLib.Variant args)
    {
        if (signal == "Activated" && args.GetTypeString() == "(osta{sv})" && args.GetChildValue(0).GetString(out _) == _session)
        {
            var role = args.GetChildValue(1).GetString(out _);
            var index = Core.Hotkeys.Roles.ToList().FindIndex(each => KeyNames.RoleOf(each) == role);
            if (index >= 0)
            {
                Pressed(index);
            }
        }
    }

    // A proxy hears a path's signals, as GirCore cannot call back a subscription of the connection's own. It starts nothing:
    // a desktop without the portal has no owner of its name, whose proxy hears nothing.
    private Gio.DBusProxy Listen(string path, string face) => Gio.DBusProxy.NewSync(
        _bus, Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart, null, Portal, path, face, null);

    private static (string, GLib.Variant) Handle(string token) => ("handle_token", GLib.Variant.NewString(token));

    private static GLib.Variant Options(params (string Key, GLib.Variant Value)[] options) => GLib.Variant.NewArray(
        GLib.VariantType.New("{sv}"),
        [.. options.Select(option => GLib.Variant.NewDictEntry(GLib.Variant.NewString(option.Key), GLib.Variant.NewVariant(option.Value)))]);
}
