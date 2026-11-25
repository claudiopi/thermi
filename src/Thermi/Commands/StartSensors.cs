using Thermi.Install;
using Windows.Win32;
using Windows.Win32.System.Services;

namespace Thermi.Commands;

/// <summary>Where the sensor service stands, as the service manager tells it.</summary>
internal enum ServiceRun
{
    Stopped,
    Running,
    Missing,

    /// <summary>Starting, stopping, pausing or paused: on its way to neither.</summary>
    Changing,
}

/// <summary>The service manager, which tests replace: the service's state, and asking it to start or stop.</summary>
internal interface IServiceManager
{
    ServiceRun State();

    /// <summary>Asks the service to start; false when Windows refuses.</summary>
    bool Start();

    /// <summary>Asks the service to stop; false when Windows refuses, as for one already stopped.</summary>
    bool Stop();

    /// <summary>The exit code the service stopped with last.</summary>
    int ExitCode();
}

/// <summary>
/// "Start sensors": the elevated mode starts <c>thermi-sensors</c> and waits up to 10 s for it to run, and the overlay
/// asks Windows for it with one UAC prompt. The mode takes its verb and nothing else, so whoever starts it cannot steer it.
/// </summary>
internal static class StartSensors
{
    public const string Verb = "start-sensors";

    public const string Service = "thermi-sensors";

    /// <summary>The exit code when the service runs, or ran already.</summary>
    public const int Started = 0;

    /// <summary>The exit code when it did not run within <see cref="Wait"/>, or Windows refused to start it.</summary>
    public const int Failed = 1;

    /// <summary>The exit code when the service is not installed, which reinstalling thermi fixes.</summary>
    public const int Missing = 3;

    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    public static async Task<int> RunAsync(IServiceManager manager, TimeProvider time)
    {
        switch (manager.State())
        {
            case ServiceRun.Missing:
                return Missing;
            case ServiceRun.Running:
                return Started;
            case ServiceRun.Stopped:
            case ServiceRun.Changing:
            default:
                break;
        }

        // One that another start beat to it runs all the same, so a refusal is judged by the state that follows.
        manager.Start();
        for (var start = time.GetTimestamp(); time.GetElapsedTime(start) < Wait; await Task.Delay(Poll, time).ConfigureAwait(false))
        {
            if (manager.State() == ServiceRun.Running)
            {
                return Started;
            }
        }

        return Failed;
    }

    /// <summary>
    /// Stops the service, waits up to <see cref="Wait"/> for it to stop, and starts it again as <see cref="RunAsync"/>
    /// does, so it opens what was installed while it ran.
    /// </summary>
    public static async Task<int> RestartAsync(IServiceManager manager, TimeProvider time)
    {
        if (manager.State() is ServiceRun.Missing)
        {
            return Missing;
        }

        manager.Stop();
        var start = time.GetTimestamp();
        while (manager.State() is not ServiceRun.Stopped)
        {
            if (time.GetElapsedTime(start) >= Wait)
            {
                return Failed;
            }

            await Task.Delay(Poll, time).ConfigureAwait(false);
        }

        return await RunAsync(manager, time).ConfigureAwait(false);
    }

    /// <summary>
    /// "Start sensors", behind one UAC prompt, as <see cref="Elevated.AskAsync(string)"/> runs it; in a per-user install,
    /// the user's own host, with none.
    /// </summary>
    public static async Task<int?> AskAsync() => StartUsersOwn() ?? await Elevated.AskAsync(Verb);

    /// <summary>
    /// In a per-user install, which has no service to start, the user's own host started with no prompt, answered as the
    /// elevated mode would; null elsewhere, where the service is the one to start.
    /// </summary>
    public static int? StartUsersOwn() => !SensorPipes.PerUser ? null : SensorPipes.StartUserHost() ? Started : Failed;
}

/// <summary>Windows' service manager, for one service, as the elevated mode reaches it.</summary>
internal sealed class WindowsServices(string name) : IServiceManager
{
    public ServiceRun State()
    {
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_QUERY_STATUS);
        if (service.IsInvalid)
        {
            return ServiceRun.Missing;
        }

        if (!PInvoke.QueryServiceStatus(service, out var status))
        {
            return ServiceRun.Changing;
        }

        return status.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING ? ServiceRun.Running
            : status.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED ? ServiceRun.Stopped
            : ServiceRun.Changing;
    }

    public int ExitCode()
    {
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_QUERY_STATUS);
        return !service.IsInvalid && PInvoke.QueryServiceStatus(service, out var status) ? (int)status.dwWin32ExitCode : StartSensors.Failed;
    }

    public bool Start()
    {
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_START);
        return !service.IsInvalid && PInvoke.StartService(service, []);
    }

    public bool Stop()
    {
        using var manager = PInvoke.OpenSCManager(null, null, PInvoke.SC_MANAGER_CONNECT);
        using var service = PInvoke.OpenService(manager, name, PInvoke.SERVICE_STOP);
        return !service.IsInvalid && PInvoke.ControlService(service, PInvoke.SERVICE_CONTROL_STOP, out _);
    }
}
