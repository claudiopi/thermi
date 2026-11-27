namespace Thermi.Linux.Desktop;

/// <summary>A notice of the desktop's own, through the freedesktop notifications every Linux desktop serves on D-Bus.</summary>
internal static class DesktopNotices
{
    public const string App = "thermi";

    /// <summary>Shows a notice; a desktop without notices shows none, and that is all.</summary>
    public static async Task Say(Gio.DBusConnection bus, string title, string text)
    {
        try
        {
            await bus.CallAsync(
                "org.freedesktop.Notifications",
                "/org/freedesktop/Notifications",
                "org.freedesktop.Notifications",
                "Notify",
                GLib.Variant.NewTuple(
                [
                    GLib.Variant.NewString(App),
                    GLib.Variant.NewUint32(0),
                    GLib.Variant.NewString(""),
                    GLib.Variant.NewString(title),
                    GLib.Variant.NewString(text),
                    GLib.Variant.NewStrv([]),
                    GLib.Variant.NewArray(GLib.VariantType.New("{sv}"), []),
                    GLib.Variant.NewInt32(-1),
                ]));
        }
        catch (GLib.GException)
        {
            // No notification server answers in this session.
        }
    }
}
