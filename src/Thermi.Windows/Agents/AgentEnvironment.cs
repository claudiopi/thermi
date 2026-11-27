using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Windows.Commands;
using Thermi.Windows.Install;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Agents;

/// <summary>
/// What an agent's command reaches on Windows, each part of which a test may replace: the sensors read from the pipe as the
/// overlay reads them, the service's registration and the installer's key, the overlay's mutex, UAC's raised token, the
/// Run key, the monitors and taskbars, the keyboard layouts' AltGr keys and a <c>thermi cpu-load</c> of its own.
/// </summary>
internal sealed record AgentEnvironment(string SettingsPath, Func<ISensorFeed> Sensors, StartWithWindows Startup, ILogger Log) : IAgentEnvironment
{
    // The services' key, which every user may read; the service's own key there is its registration.
    private const string Services = @"SYSTEM\CurrentControlSet\Services\";

    // Written by the installer: 1 when it installed the sensors alone.
    private const string InstallKey = @"SOFTWARE\thermi";

    public const string SensorsOnlyValue = "SensorsOnly";

    public Func<bool> Installed { get; init; } = () => Exists(Services + StartSensors.Service);

    /// <summary>Whether this is a per-user install with no service, whose own host the sensors are read from.</summary>
    public Func<bool> PerUser { get; init; } = () => SensorPipes.UserPipe is not null && !Exists(Services + StartSensors.Service);

    /// <summary>Whether an overlay runs in this session: its single-instance mutex is there, which is looked at, never taken.</summary>
    public Func<bool> Overlay { get; init; } = () => NamedMutex.Exists(HostEnvironment.OverlayMutexName);

    public Func<bool> SensorsOnly { get; init; } = () =>
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKey);
        return key?.GetValue(SensorsOnlyValue) is 1;
    };

    /// <summary>Whether this process runs the installed version, so a newer settings file is a rollback to it.</summary>
    public Func<bool> RunsInstalled { get; init; } = ThermiPaths.RunsInstalled;

    /// <summary>Whether this process runs with a token UAC raised, as from an elevated terminal, which only reads.</summary>
    public Func<bool> Raised { get; init; } = Elevation.Raised;

    public Func<IReadOnlyList<Screen>> Screens { get; init; } = Desktop.Screens;

    public Func<IReadOnlySet<int>> AltGrKeys { get; init; } = HotkeyRegistry.AltGrKeys;

    // The window's load: a thermi cpu-load of its own, at idle priority, so the PC stays as it was for the person using it.
    public Action<CancellationToken> Load { get; init; } =
        stop => CpuLoad.Start(ThermiPaths.Own, CpuLoad.Arguments((int)FanFinder.MaxRun.TotalSeconds), stop);

    public string FinderRan { get; init; } = Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    IStartAtSignIn IAgentEnvironment.Startup => Startup;

    public static AgentEnvironment Of(ILoggerFactory logs, TimeProvider time)
    {
        return new(
            ConfigStore.DefaultPath,
            () => new SystemFeed(
                new SensorClient(time, logs.CreateLogger<SensorClient>(), new SensorPipes(time, logs.CreateLogger<SensorPipes>(), starts: false)), time),
            StartWithWindows.Of(ThermiPaths.Launcher),
            logs.CreateLogger("Thermi.Agents"));
    }

    ISensorFeed IAgentEnvironment.Sensors() => Sensors();

    bool IAgentEnvironment.Installed() => Installed();

    bool IAgentEnvironment.PerUser() => PerUser();

    bool IAgentEnvironment.Overlay() => Overlay();

    bool IAgentEnvironment.SensorsOnly() => SensorsOnly();

    bool IAgentEnvironment.Raised() => Raised();

    bool IAgentEnvironment.RunsInstalled() => RunsInstalled();

    IReadOnlyList<Screen> IAgentEnvironment.Screens() => Screens();

    IReadOnlySet<int> IAgentEnvironment.AltGrKeys() => AltGrKeys();

    void IAgentEnvironment.Load(CancellationToken stop) => Load(stop);

    // Only a share or a lock another process holds: any other failure is no run under way elsewhere.
    bool IAgentEnvironment.HeldElsewhere(IOException error) =>
        (WIN32_ERROR)(error.HResult & 0xFFFF) is WIN32_ERROR.ERROR_SHARING_VIOLATION or WIN32_ERROR.ERROR_LOCK_VIOLATION;

    private static bool Exists(string key)
    {
        using var found = Registry.LocalMachine.OpenSubKey(key);
        return found is not null;
    }
}
