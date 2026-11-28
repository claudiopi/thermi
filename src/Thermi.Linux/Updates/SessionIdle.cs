using Microsoft.Extensions.Logging;

namespace Thermi.Linux.Updates;

/// <summary>
/// What the desktop says over the session's bus of the person's input and lock, which Wayland tells no other program: the
/// time since the last input from Mutter's idle monitor on GNOME, else from the freedesktop screensaver; locked as either
/// screensaver says it is active. KDE Plasma on Wayland answers that it tells no idle time, and Hyprland and Sway serve
/// no screensaver: there an update hands over only at a lock, a sign-out or every overlay hidden. A call the desktop does
/// not serve is an answer it gives each time, logged once. Each call waits half a second at most.
/// </summary>
internal sealed partial class SessionIdle(Gio.DBusConnection bus, ILogger log)
{
    private const int AnswerWithin = 500;

    private const string FreedesktopSaver = "org.freedesktop.ScreenSaver";

    private const string GnomeSaver = "org.gnome.ScreenSaver";

    // GDBus names a peer's error in its message, "GDBus.Error:<name>: <text>": no one on the bus serves the call, or a peer
    // serves it and says it cannot.
    private static readonly string[] NotServedErrors =
    [
        "GDBus.Error:org.freedesktop.DBus.Error.ServiceUnknown:",
        "GDBus.Error:org.freedesktop.DBus.Error.NotSupported:",
        "GDBus.Error:org.freedesktop.DBus.Error.UnknownMethod:",
    ];

    // Each call by its interface and method, once logged as not served.
    private readonly HashSet<string> _told = [];

    // The freedesktop spec says seconds, where Plasma on X11, which serves it, answers in milliseconds: read as those, a
    // desktop that answers in seconds is never idle long enough, where read as seconds, Plasma's would hand over under the
    // person's hands.
    public TimeSpan SinceInput() =>
        Read("org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core", "org.gnome.Mutter.IdleMonitor", "GetIdletime", "(t)")
            is { } mutter ? TimeSpan.FromMilliseconds(mutter.GetChildValue(0).GetUint64())
        : Read(FreedesktopSaver, "/org/freedesktop/ScreenSaver", FreedesktopSaver, "GetSessionIdleTime", "(u)") is { } saver
            ? TimeSpan.FromMilliseconds(saver.GetChildValue(0).GetUint32())
        : TimeSpan.Zero;

    public bool Locked() =>
        (Read(GnomeSaver, "/org/gnome/ScreenSaver", GnomeSaver, "GetActive", "(b)")
            ?? Read(FreedesktopSaver, "/org/freedesktop/ScreenSaver", FreedesktopSaver, "GetActive", "(b)"))?.GetChildValue(0).GetBoolean() == true;

    private GLib.Variant? Read(string name, string path, string face, string method, string reply)
    {
        try
        {
            return bus.CallSync(name, path, face, method, null, GLib.VariantType.New(reply), Gio.DBusCallFlags.NoAutoStart, AnswerWithin, null);
        }
        catch (GLib.GException ex)
        {
            if (Array.Exists(NotServedErrors, error => ex.Message.StartsWith(error, StringComparison.Ordinal)) && _told.Add($"{face}.{method}"))
            {
                NotServed(log, face, method);
            }

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "This desktop does not serve {Face}.{Method}, which tells an update a quiet moment to hand over at")]
    private static partial void NotServed(ILogger logger, string face, string method);
}
