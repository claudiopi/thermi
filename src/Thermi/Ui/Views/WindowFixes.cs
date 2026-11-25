using System.ComponentModel;
using Thermi.Commands;
using Thermi.Core.Bridge;
using Thermi.Core.Install;
using Thermi.Core.Sensors;
using Thermi.Install;
using Thermi.Logging;
using Thermi.Ui.Web;
using Thermi.Updates;
using Windows.Win32;

namespace Thermi.Ui.Views;

/// <summary>
/// The two fixes the window runs through thermi's elevated mode, behind one UAC prompt each: installing the driver and
/// starting the sensor service (docs/adr/0010-driver-and-service-from-the-window.md).
/// </summary>
internal sealed class WindowFixes(WindowEnvironment environment, TimeProvider time)
{
    // The sensor service restarts after the driver's install, and its pipe comes back within a few reads.
    private static readonly TimeSpan DriverReadsWithin = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DriverPoll = TimeSpan.FromMilliseconds(500);

    // A download of the machine MSI, the prompt and the install, on a slow network.
    private static readonly TimeSpan InstallWithin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Installed only once a CPU temperature, power or clock reads, after the sensor service opened the new driver; a
    /// driver that loads only once Windows restarts says so, since nothing reads before then.
    /// </summary>
    public async Task<DriverInstall> InstallDriver()
    {
        if (SensorPipes.PerUser)
        {
            return await InstallForEveryoneAsync();
        }

        var code = await ElevateAsync(Commands.InstallDriver.Verb);
        if (code is not Commands.InstallDriver.Installed)
        {
            return code is Commands.InstallDriver.RestartNeeded ? DriverInstall.RestartWindows : DriverInstall.NotInstalled;
        }

        var start = time.GetTimestamp();
        for (; time.GetElapsedTime(start) < DriverReadsWithin; await Task.Delay(DriverPoll, time))
        {
            if (environment.Sensors.Latest is { } snapshot && !SensorMap.DriverMayBeMissing(snapshot))
            {
                return DriverInstall.Installed;
            }
        }

        return DriverInstall.NotInstalled;
    }

    // A per-user install has neither the service nor the driver: its host installs thermi for everyone, behind one prompt
    // that names thermi, and the window waits for what it came to (docs/adr/0016-admin-on-demand.md).
    private async Task<DriverInstall> InstallForEveryoneAsync()
    {
        var asked = time.GetUtcNow();
        try
        {
            // The prompt msiexec shows comes to the front, as the click that asked allows.
            PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
            UpdatesHere.Tell(InstallForEveryone.Code);
        }
        catch (InvalidOperationException ex)
        {
            environment.Log.ServiceNotTold(InstallForEveryone.Code, ex.Message);
            return DriverInstall.NotInstalled;
        }

        var start = time.GetTimestamp();
        for (; time.GetElapsedTime(start) < InstallWithin; await Task.Delay(DriverPoll, time))
        {
            if (UpdatesHere.MachineInstall() is { } done && done.At >= asked && done.Outcome != MachineInstallOutcome.Running)
            {
                if (done.Outcome == MachineInstallOutcome.DownloadPage)
                {
                    WindowLinks.OpenUrl(WindowLinks.Downloads, environment.Log);
                }

                return done.Outcome == MachineInstallOutcome.Installed ? DriverInstall.Installed : DriverInstall.NotInstalled;
            }
        }

        return DriverInstall.NotInstalled;
    }

    // The window recovers by seeing the pipe come back, whatever the answer.
    // In a per-user install the user's own host is the one to start, which needs no prompt.
    public async Task<bool> StartService() => SensorPipes.PerUser ? SensorPipes.StartUserHost() : await ElevateAsync(StartSensors.Verb) is StartSensors.Started;

    // Starting a process behind UAC blocks its caller until the prompt closes, so it runs off the window's thread. Null
    // when the prompt was refused or the mode could not start.
    private async Task<int?> ElevateAsync(string verb)
    {
        try
        {
            var code = await Task.Run(() => environment.Elevate(verb));
            if (code is { } ended and not 0)
            {
                environment.Log.ElevatedEnded(verb, ended);
            }

            return code;
        }
        catch (Win32Exception ex)
        {
            environment.Log.ElevatedFailed(verb, ex);
            return null;
        }
    }
}
