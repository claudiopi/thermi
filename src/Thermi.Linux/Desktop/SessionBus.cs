namespace Thermi.Linux.Desktop;

/// <summary>
/// The session's D-Bus, through Gio, for what thermi-cli asks the desktop: whether a name has an owner, and a method's
/// answer. A session with no bus, or a PC without Gio, as a server or a container, answers nothing, never a crash.
/// </summary>
internal static class SessionBus
{
    private const int TimeoutMilliseconds = 2000;

    private static readonly Lazy<Gio.DBusConnection?> Connection = new(Connect);

    public static bool HasOwner(string name) =>
        Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", name) is { } answer
        && answer.GetChildValue(0).GetBoolean();

    /// <summary>
    /// A method's answer to <paramref name="arguments"/>, or null where there is no bus, nobody serves the name, or the call
    /// fails. Its arguments are made only once Gio has loaded, which a PC without it never does.
    /// </summary>
    public static GLib.Variant? Call(string name, string path, string face, string method, params string[] arguments)
    {
        if (Connection.Value is not { } bus)
        {
            return null;
        }

        try
        {
            var parameters = arguments.Length > 0 ? GLib.Variant.NewTuple([.. arguments.Select(GLib.Variant.NewString)]) : null;
            return bus.CallSync(name, path, face, method, parameters, null, Gio.DBusCallFlags.None, TimeoutMilliseconds, null);
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    /// <summary>A value of an <c>a{sv}</c> dictionary, as D-Bus answers hold them, or null when it holds none of that key.</summary>
    public static GLib.Variant? Lookup(GLib.Variant dictionary, string key)
    {
        for (nuint index = 0; index < dictionary.NChildren(); index++)
        {
            var entry = dictionary.GetChildValue(index);
            if (entry.GetChildValue(0).GetString(out _) == key)
            {
                return entry.GetChildValue(1).GetVariant();
            }
        }

        return null;
    }

    private static Gio.DBusConnection? Connect()
    {
        try
        {
            Gio.Module.Initialize();
            return Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        }
        catch (Exception ex) when (ex is GLib.GException or DllNotFoundException or TypeInitializationException)
        {
            return null;
        }
    }
}
