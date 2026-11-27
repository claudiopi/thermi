using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.Window;

/// <summary>
/// The window's view of the overlay's process: running while it owns its name on the session's bus, which GDBus watches
/// and keeps, so asking costs no call, and tells when it starts or ends; ended through the <c>quit</c> it exports there.
/// Used on GTK's loop alone, <see cref="HotkeyStatus"/> aside.
/// </summary>
internal sealed class OverlayLink : IDisposable
{
    // The overlay answers on its main loop, which a paint holds for a few milliseconds at most.
    private const int AnswerWithin = 2000;

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

    /// <summary>
    /// Asks the running overlay which of its hotkeys the desktop refused, as <see cref="Core.Hotkeys.Status"/> says it; null
    /// when none runs or it does not answer. GDBus takes the call from any thread, the window's own aside.
    /// </summary>
    public long? HotkeyStatus()
    {
        if (_bus is null || !Running)
        {
            return null;
        }

        try
        {
            using var answer = _bus.CallSync(
                ShellLink.Name, OverlayObject.Path, OverlayObject.Name, OverlayObject.HotkeyStatus, null, GLib.VariantType.New("(x)"),
                Gio.DBusCallFlags.NoAutoStart, AnswerWithin, null);
            return answer.GetChildValue(0).GetInt64();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    public void Quit()
    {
        if (_bus is not null && Running)
        {
            Gio.DBusActionGroup.Get(_bus, ShellLink.Name, ShellLink.ObjectPath).ActivateAction("quit", null);
        }
    }

    public void Dispose() => _owner?.Dispose();
}
