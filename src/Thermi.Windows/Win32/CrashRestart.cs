using Microsoft.Extensions.Logging;
using Thermi.Cli.Commands;
using Thermi.Windows.Logging;
using Windows.Win32;
using Windows.Win32.System.Recovery;

namespace Thermi.Windows.Win32;

/// <summary>
/// The overlay's registration with Windows Error Reporting, so an overlay that crashed or hung after running a minute or
/// more is started again as it ran (docs/adr/0017-windows-restarts-the-overlay.md). Never after an update of thermi or of
/// Windows: the installer and the Run key start it then, and a second thermi would open the window.
/// </summary>
internal static class CrashRestart
{
    private const REGISTER_APPLICATION_RESTART_FLAGS Flags =
        REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_PATCH | REGISTER_APPLICATION_RESTART_FLAGS.RESTART_NO_REBOOT;

    /// <summary>Registers this process, with <c>--trace</c> when it traces: Windows adds the executable itself.</summary>
    public static void Register(bool trace, ILogger log)
    {
        var result = PInvoke.RegisterApplicationRestart(trace ? ThermiCommandLine.TraceOption : null, Flags);
        if (result.Failed)
        {
            log.RestartRefused(result.Value);
        }
    }
}
