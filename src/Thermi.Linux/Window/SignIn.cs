using Thermi.Cli.Commands;
using Thermi.Linux.Agents;

namespace Thermi.Linux.Window;

/// <summary><c>thermi sign-in</c>, which the package's autostart entry and Sway's config.d run at each sign-in.</summary>
internal sealed record SignInRequest : IThermiRequest;

/// <summary>
/// The overlay at sign-in where the desktop starts no graphical-session.target for systemd, the one thing that starts
/// <c>thermi.service</c>: Xfce, MATE, Cinnamon and Sway run their autostart, and this, but bring no target. Where start at
/// sign-in is on, it waits a moment for the target, which GNOME and Plasma bring, maybe after their autostart, then
/// starts the unit, which already runs or would; and only where the target never came, the window's unit of its own. So
/// it never starts a second overlay, and ends once the overlay started.
/// </summary>
internal static class SignIn
{
    public const string Command = "sign-in";

    /// <summary>How long a desktop has to bring its graphical session up for systemd, as GNOME and Plasma do in a moment.</summary>
    public static readonly TimeSpan SessionBound = TimeSpan.FromSeconds(10);

    public static async Task<int> RunAsync(TimeProvider time)
    {
        var unit = new UserUnit(time);
        if (!await unit.OnAsync(CancellationToken.None))
        {
            return 0;
        }

        await unit.SessionUpAsync(SessionBound, CancellationToken.None);
        return await LinuxFixes.StartOverlay(Environment.ProcessPath ?? "/usr/bin/thermi") ? 0 : 1;
    }
}
