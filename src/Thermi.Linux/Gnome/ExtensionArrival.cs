using Microsoft.Extensions.Logging;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Gnome;

/// <summary>
/// Whether the overlays may show through the Shell's extension, and what thermi does while not: missing, or installed but
/// not read yet by the Shell, it asks GNOME to install it from extensions.gnome.org, which GNOME asks the person and which
/// shows the overlays at once; missing, off or without the overlay's own calls, nothing shows, and one notice of the
/// desktop's says why, once a run, with a button that asks GNOME again; not read yet, only when GNOME did not install it
/// does the notice say to sign in again, with no button. Once the extension runs, as its Ready says, the overlays show
/// through it: through an earlier protocol's too, as runs from an update until the next sign-in, whose notice says to sign
/// in again, so no update leaves the overlays blank.
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
    private readonly Action<Action> _post;
    private bool _told;

    // The Shell's answer to an install comes on a thread of its own: post runs what follows on the loop that made the arrival.
    public ExtensionArrival(ShellLink link, DesktopNotices notices, Func<Texts> texts, ILogger log, Action<Action> post)
    {
        (_link, _notices, _texts, _log, _post) = (link, notices, texts, log, post);
        notices.Invoked += Clicked;
    }

    public bool Ready { get; private set; }

    /// <summary>Asks where the extension stands, as thermi starts.</summary>
    public void Start()
    {
        var state = _link.State();
        Ready = state is ShellExtensionState.Ours or ShellExtensionState.Earlier;
        if (state == ShellExtensionState.Missing)
        {
            _link.Install();
        }
        else if (state == ShellExtensionState.Unread)
        {
            _ = InstallUnreadAsync();
        }

        if (state is not ShellExtensionState.Ours and not ShellExtensionState.Starting and not ShellExtensionState.Unread)
        {
            Tell(state);
        }
    }

    /// <summary>
    /// The extension came up, as the Shell started or the person installed it: true when the overlays show through it. Its
    /// Ready tells a protocol in thermi's shape alone, so what it offers is asked again.
    /// </summary>
    public bool Arrived()
    {
        var state = _link.State();
        Ready = state is ShellExtensionState.Ours or ShellExtensionState.Earlier;
        if (state is ShellExtensionState.Earlier or ShellExtensionState.OtherVersion)
        {
            Tell(state);
        }

        return Ready;
    }

    /// <summary>The extension stopped, as while the screen is locked: nothing shows, and no notice says so, until it is back.</summary>
    public void Left() => Ready = false;

    // The copy from extensions.gnome.org shows the overlays at once, where the package's waits for the next sign-in.
    private async Task InstallUnreadAsync()
    {
        try
        {
            using var answer = await _link.InstallAsync().ConfigureAwait(false);
        }
        catch (GLib.GException)
        {
            // Declined, offline, or not on the site: what the Shell has now says which.
        }

        if (_link.State() == ShellExtensionState.Unread)
        {
            _post(() => Tell(ShellExtensionState.Unread));
        }
    }

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

    private void Tell(ShellExtensionState state)
    {
        if (state == ShellExtensionState.Earlier)
        {
            _log.ExtensionEarlier(ShellLink.Protocol);
        }
        else
        {
            _log.ExtensionNotReady(state, ShellLink.Protocol);
        }

        if (_told)
        {
            return;
        }

        _told = true;
        var said = _texts();
        var (text, button) = state switch
        {
            ShellExtensionState.Missing => (HostText.GnomeExtensionMissing, ((string, HostText)?)(InstallAction, HostText.GnomeExtensionInstall)),
            ShellExtensionState.OtherVersion => (HostText.GnomeExtensionOtherVersion, (UpdateAction, HostText.GnomeExtensionUpdate)),
            ShellExtensionState.Unread => (HostText.GnomeExtensionNextSignIn, null),
            ShellExtensionState.Earlier => (HostText.GnomeExtensionEarlier, null),
            ShellExtensionState.Off or ShellExtensionState.Ours or ShellExtensionState.Starting => (HostText.GnomeExtensionOff, null),
            _ => (HostText.GnomeExtensionOff, null),
        };
        (string, string)[] actions = button is var (id, label) ? [(id, said.Of(label))] : [];
        var title = state == ShellExtensionState.Earlier ? HostText.GnomeExtensionEarlierTitle : HostText.GnomeExtensionTitle;
        _ = _notices.Say(said.Of(title), said.Of(text), actions);
    }
}
