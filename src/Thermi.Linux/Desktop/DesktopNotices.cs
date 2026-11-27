namespace Thermi.Linux.Desktop;

/// <summary>
/// Notices of the desktop's own, through the freedesktop notifications every Linux desktop serves on D-Bus, with buttons
/// named by action ids, which tests match, never by their texts.
/// </summary>
internal sealed class DesktopNotices : IDisposable
{
    public const string App = "thermi";

    private const string Name = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";

    private readonly Gio.DBusConnection _bus;
    private readonly Gio.DBusProxy _signals;
    private readonly HashSet<uint> _ours = [];

    public DesktopNotices(Gio.DBusConnection bus)
    {
        _bus = bus;
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart;
        _signals = Gio.DBusProxy.NewSync(bus, Flags, null, Name, ObjectPath, Name, null);
        _signals.OnGSignal += (_, heard) => Heard(heard.SignalName, heard.Parameters);
    }

    /// <summary>A button of one of thermi's notices was clicked, by its action id.</summary>
    public Action<string> Invoked { get; set; } = _ => { };

    /// <summary>Shows a notice with its buttons, each an action id and its text; a desktop without notices shows none, and that is all.</summary>
    public async Task Say(string title, string text, params (string Id, string Label)[] actions)
    {
        try
        {
            var answer = await _bus.CallAsync(
                Name,
                ObjectPath,
                Name,
                "Notify",
                GLib.Variant.NewTuple(
                [
                    GLib.Variant.NewString(App),
                    GLib.Variant.NewUint32(0),
                    GLib.Variant.NewString(""),
                    GLib.Variant.NewString(title),
                    GLib.Variant.NewString(text),
                    GLib.Variant.NewStrv([.. actions.SelectMany(action => new[] { action.Id, action.Label })]),
                    GLib.Variant.NewArray(GLib.VariantType.New("{sv}"), []),
                    GLib.Variant.NewInt32(-1),
                ]));
            _ours.Add(answer.GetChildValue(0).GetUint32());
        }
        catch (GLib.GException)
        {
            // No notification server answers in this session.
        }
    }

    public void Dispose() => _signals.Dispose();

    private void Heard(string signal, GLib.Variant args)
    {
        if (signal == "ActionInvoked" && _ours.Contains(args.GetChildValue(0).GetUint32()))
        {
            Invoked(args.GetChildValue(1).GetString(out _));
        }
    }
}
