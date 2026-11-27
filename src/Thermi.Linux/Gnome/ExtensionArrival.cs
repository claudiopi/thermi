using Microsoft.Extensions.Logging;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Gnome;

/// <summary>
/// Whether the overlays may show through the Shell's extension, and what thermi does while not: missing, it asks GNOME to
/// install it, which GNOME asks the person; missing, off or of another protocol, nothing shows, and one notice of the
/// desktop's says why, once a run, with a button that asks GNOME again. Once the extension runs thermi's protocol, as its
/// Ready says, the overlays show.
/// </summary>
internal sealed class ExtensionArrival
{
    /// <summary>The missing extension's notice's button: GNOME is asked to install it again.</summary>
    public const string InstallAction = "install-extension";

    /// <summary>The button of the notice of another protocol's extension: GNOME looks for its update.</summary>
    public const string UpdateAction = "update-extension";

    private readonly ShellLink _link;
    private readonly DesktopNotices _notices;
    private readonly Func<Texts> _texts;
    private readonly ILogger _log;
    private bool _told;

    public ExtensionArrival(ShellLink link, DesktopNotices notices, Func<Texts> texts, ILogger log)
    {
        (_link, _notices, _texts, _log) = (link, notices, texts, log);
        notices.Invoked += Clicked;
    }

    /// <summary>Whether the extension runs thermi's protocol now.</summary>
    public bool Ready { get; private set; }

    /// <summary>Asks where the extension stands, as thermi starts.</summary>
    public void Start()
    {
        var state = _link.State();
        Ready = state == ExtensionState.Ours;
        if (state == ExtensionState.Missing)
        {
            _link.Install();
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

    /// <summary>The extension stopped, as while the screen is locked: nothing shows, and no notice says so, until it is back.</summary>
    public void Left() => Ready = false;

    private void Clicked(uint notice, string action)
    {
        if (action == InstallAction)
        {
            _link.Install();
        }
        else if (action == UpdateAction)
        {
            _link.CheckForUpdates();
        }
    }

    private void Tell(ExtensionState state)
    {
        _log.ExtensionNotReady(state, ShellLink.Protocol);
        if (_told)
        {
            return;
        }

        _told = true;
        var said = _texts();
        var (text, button) = state switch
        {
            ExtensionState.Missing => (HostText.GnomeExtensionMissing, ((string, HostText)?)(InstallAction, HostText.GnomeExtensionInstall)),
            ExtensionState.OtherVersion => (HostText.GnomeExtensionOtherVersion, (UpdateAction, HostText.GnomeExtensionUpdate)),
            ExtensionState.Off or ExtensionState.Ours or ExtensionState.Starting => (HostText.GnomeExtensionOff, null),
            _ => (HostText.GnomeExtensionOff, null),
        };
        (string, string)[] actions = button is var (id, label) ? [(id, said.Of(label))] : [];
        _ = _notices.Say(said.Of(HostText.GnomeExtensionTitle), said.Of(text), actions);
    }
}
