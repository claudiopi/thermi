using Thermi.Linux.Gnome;

namespace Thermi.Linux.Window;

/// <summary>
/// The window's view of the overlay's process: running while it owns its name on the session's bus, which GDBus watches
/// and keeps, so asking costs no call, and tells when it starts or ends; ended through the <c>quit</c> it exports there.
/// Used on GTK's loop alone.
/// </summary>
internal sealed class OverlayLink : IDisposable
{
    private readonly Gio.DBusConnection? _bus;
    private readonly Gio.DBusProxy? _owner;

    public OverlayLink(Gio.DBusConnection? bus, Action changed)
    {
        _bus = bus;
        if (bus is null)
        {
            return;
        }

        // A proxy's owner comes through GObject's notify, which GirCore marshals, where a name watch's callback it does not.
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotConnectSignals | Gio.DBusProxyFlags.DoNotAutoStart;
        _owner = Gio.DBusProxy.NewSync(bus, Flags, null, ShellLink.Name, ShellLink.ObjectPath, "org.gtk.Actions", null);
        _owner.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "g-name-owner")
            {
                changed();
            }
        };
    }

    public bool Running => _owner?.GetNameOwner() is not null;

    public void Quit()
    {
        if (_bus is not null && Running)
        {
            Gio.DBusActionGroup.Get(_bus, ShellLink.Name, ShellLink.ObjectPath).ActivateAction("quit", null);
        }
    }

    public void Dispose() => _owner?.Dispose();
}
