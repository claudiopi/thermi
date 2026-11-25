using Windows.Win32;
using Windows.Win32.Security;

namespace Thermi.Ui;

/// <summary>
/// Whether this process runs with an administrator's token that UAC raised over a limited one, as an elevated console
/// gives it (docs/adr/0016-admin-on-demand.md). Such a process took its environment from the user's registry, so the
/// overlay and the window refuse it; SYSTEM, the elevated mode's account, and an administrator with UAC off have the
/// default type and run.
/// </summary>
internal static class Elevation
{
    /// <summary>What a refused start says on the terminal's error stream, the only place it writes.</summary>
    public const string Refusal =
        "thermi was started elevated, so it exits: its overlay and settings window run only unelevated. Start it from Start or a terminal that is not elevated.";

    public static unsafe bool Raised()
    {
        // A token can always be queried by its own process, so this fails only where nothing is to be learned; refusing
        // then would stop the overlay without a word.
        using var self = PInvoke.GetCurrentProcess_SafeHandle();
        if (!PInvoke.OpenProcessToken(self, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return false;
        }

        using (token)
        {
            TOKEN_ELEVATION_TYPE type;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevationType, &type, sizeof(TOKEN_ELEVATION_TYPE), out _)
                && type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
        }
    }
}
