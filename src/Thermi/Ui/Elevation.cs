using System.Diagnostics;
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
    public static unsafe bool Raised()
    {
        using var self = Process.GetCurrentProcess();
        if (!PInvoke.OpenProcessToken(self.SafeHandle, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
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
