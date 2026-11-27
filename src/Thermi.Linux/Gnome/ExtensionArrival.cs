using Microsoft.Extensions.Logging;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Gnome;

/// <summary>
/// Whether the overlays may show through the Shell's extension, and what thermi does while not: missing, it asks GNOME to
/// install it, which GNOME asks the person; missing, off or of another protocol, nothing shows, and one notice of the
/// desktop's says why, once a run. Once the extension runs thermi's protocol, as its Ready says, the overlays show.
/// </summary>
internal sealed class ExtensionArrival(ShellLink link, Gio.DBusConnection bus, Func<Texts> texts, ILogger log)
{
    private bool _told;

    /// <summary>Whether the extension runs thermi's protocol now.</summary>
    public bool Ready { get; private set; }

    /// <summary>Asks where the extension stands, as thermi starts.</summary>
    public void Start()
    {
        var state = link.State();
        Ready = state == ExtensionState.Ours;
        if (state == ExtensionState.Missing)
        {
            link.Install();
        }

        if (state is not ExtensionState.Ours and not ExtensionState.Starting)
        {
            Tell(state);
        }
    }

    /// <summary>The extension came up, as the Shell started or the person installed it: true when it speaks thermi's protocol.</summary>
    public bool Arrived(uint protocol)
    {
        Ready = protocol == ShellLink.Protocol;
        if (!Ready)
        {
            Tell(ExtensionState.OtherVersion);
        }

        return Ready;
    }

    private void Tell(ExtensionState state)
    {
        log.ExtensionNotReady(state, ShellLink.Protocol);
        if (_told)
        {
            return;
        }

        _told = true;
        var said = texts();
        var text = state switch
        {
            ExtensionState.Missing => HostText.GnomeExtensionMissing,
            ExtensionState.OtherVersion => HostText.GnomeExtensionOtherVersion,
            ExtensionState.Off or ExtensionState.Ours or ExtensionState.Starting => HostText.GnomeExtensionOff,
            _ => HostText.GnomeExtensionOff,
        };
        _ = DesktopNotices.Say(bus, said.Of(HostText.GnomeExtensionTitle), said.Of(text));
    }
}
