namespace Thermi.Linux.Updates;

/// <summary>
/// What the desktop says over the session's bus of the person's input and lock, which Wayland tells no other program: the
/// time since the last input from Mutter's idle monitor on GNOME, else from the freedesktop screensaver, which KDE Plasma
/// counts in milliseconds; locked as either screensaver says it is active. A desktop that serves neither has no idle time
/// and no lock to tell, and there only every overlay hidden is quiet. Each call waits half a second at most.
/// </summary>
internal sealed class SessionIdle(Gio.DBusConnection bus)
{
    private const int AnswerWithin = 500;

    private const string FreedesktopSaver = "org.freedesktop.ScreenSaver";

    private const string GnomeSaver = "org.gnome.ScreenSaver";

    // The spec says seconds, where Plasma, which serves it, answers in milliseconds: read as those, a desktop that answers
    // in seconds is never idle long enough, where read as seconds, Plasma's would hand over under the person's hands.
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
        catch (GLib.GException)
        {
            return null;
        }
    }
}
