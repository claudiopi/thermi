using System.ComponentModel;
using Thermi.Commands;
using Thermi.Core.Bridge;
using Thermi.Core.Sensors;
using Thermi.Logging;

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

    /// <summary>
    /// Installed only once a CPU temperature, power or clock reads, after the sensor service opened the new driver; a
    /// driver that loads only once Windows restarts says so, since nothing reads before then.
    /// </summary>
    public async Task<DriverInstall> InstallDriver()
    {
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

    // The window recovers by seeing the pipe come back, whatever the answer.
    public async Task<bool> StartService() => await ElevateAsync(StartSensors.Verb) is StartSensors.Started;

    // Starting a process behind UAC blocks its caller until the prompt closes, so it runs off the window's thread. Null
    // when the prompt was refused or the mode could not start.
    private async Task<int?> ElevateAsync(string verb)
    {
        try
        {
            var code = await Task.Run(() => environment.Elevate(environment.ExePath, verb));
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
