using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Tests.Shared;

/// <summary>
/// A line thermi writes on its standard error under <c>--trace</c> once a thing a test waits for has happened: its time on
/// the system's monotonic clock, in seconds, the event's name, and what it is about, as <c>812.034 window-shown cpu.load</c>.
/// A test arms the wait before what makes the event, so the line cannot pass unseen, and never waits for a time; lines of
/// another shape are thermi's own log, and no event.
/// </summary>
internal sealed partial record Traced(double At, string Event, string About)
{
    /// <summary>
    /// thermi took the service's next second and painted what it shows for it, the overlays' and the icons': once a second,
    /// whether an overlay is on screen or not, so a test counts thermi's own seconds.
    /// </summary>
    public const string Paint = "paint";

    /// <summary>
    /// Every overlay that is not hidden is on screen as painted: once as thermi starts, when none shows too, and each time
    /// they come back after <see cref="Hidden"/>.
    /// </summary>
    public const string Shown = "shown";

    /// <summary>The overlays went off the screen: Hide overlays, or the toggle hotkey.</summary>
    public const string Hidden = "hidden";

    /// <summary>One change of the settings is saved and on screen, wherever it came from; a notch of the wheel that resizes is one.</summary>
    public const string EditApplied = "edit-applied";

    /// <summary>A hold lifted a pill or an item, which follows the pointer from now on.</summary>
    public const string Lifted = "lifted";

    /// <summary>A drag or a hold ended, let go or taken back with Esc, and what it left is saved and on screen.</summary>
    public const string Dropped = "dropped";

    /// <summary>An overlay with Fade near the pointer reached its faded look.</summary>
    public const string Faded = "faded";

    /// <summary>The settings window showed or came to the front, about the item it shows, or about nothing.</summary>
    public const string WindowShown = "window-shown";

    /// <summary>A hotkey thermi could not take, as another program holds it, about its role as the settings name it.</summary>
    public const string HotkeyRefused = "hotkey-refused";

    /// <summary>The desktop gave none of the hotkeys: it has no way to give them, or the person said no.</summary>
    public const string HotkeysRefused = "hotkeys-refused";

    /// <summary>
    /// The <paramref name="count"/>th <paramref name="name"/> that <paramref name="stream"/> writes from this call on: armed
    /// at once, before the call returns.
    /// </summary>
    public static async Task<Traced> NextAsync(Lines stream, string name, CancellationToken cancel, int count = 1) =>
        Of(await stream.NextAsync(line => Of(line)?.Event == name, $"\"<at> {name}\"", cancel, count).ConfigureAwait(false))!;

    /// <summary>The first <paramref name="name"/> <paramref name="stream"/> ever wrote, or writes next: what a program says as it starts.</summary>
    public static async Task<Traced> FirstAsync(Lines stream, string name, CancellationToken cancel) =>
        Of(await stream.FirstAsync(line => Of(line)?.Event == name, $"\"<at> {name}\"", cancel).ConfigureAwait(false))!;

    public static Traced? Of(string line) => Line().Match(line) is { Success: true } match
        ? new Traced(double.Parse(match.Groups["at"].Value, CultureInfo.InvariantCulture), match.Groups["event"].Value, match.Groups["about"].Value)
        : null;

    [GeneratedRegex(@"^(?<at>\d+\.\d+) (?<event>[a-z]+(-[a-z]+)*)( (?<about>\S+))?$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Line();
}
