using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Windows.Commands;
using Thermi.Windows.Install;
using Thermi.Windows.Logging;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;

namespace Thermi.Windows.Agents;

/// <summary>
/// What an agent's command reaches outside its process, which tests replace: the settings file, the sensors, read from the
/// pipe as the overlay reads them with a new connection for each read, whether the service is installed or this is a per-user
/// install, whether an overlay runs, whether this install is the sensors alone, the start at sign-in, the monitors, the keys
/// AltGr types with, and the fan finder's load and the file that says when it last ran.
/// </summary>
internal sealed record AgentEnvironment(string SettingsPath, Func<ISensorFeed> Sensors, StartWithWindows Startup, ILogger Log)
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

    /// <summary>Whether this process runs with a token UAC raised, as from an elevated terminal, which only reads.</summary>
    public Func<bool> Raised { get; init; } = Elevation.Raised;

    public Func<IReadOnlyList<Screen>> Screens { get; init; } = Desktop.Screens;

    public Func<IReadOnlySet<int>> AltGrKeys { get; init; } = HotkeyRegistry.AltGrKeys;

    // The window's load: a thermi cpu-load of its own, at idle priority, so the PC stays as it was for the person using it.
    public Action<CancellationToken> Load { get; init; } =
        stop => CpuLoad.Start(ThermiPaths.Own, (int)FanFinder.MaxRun.TotalSeconds, stop);

    public string FinderRan { get; init; } = Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    public string StylesFolder => ConfigStore.StylesFolder(SettingsPath);

    public static AgentEnvironment Of(ILoggerFactory logs, TimeProvider time)
    {
        return new(
            ConfigStore.DefaultPath,
            () => new SystemFeed(
                new SensorClient(time, logs.CreateLogger<SensorClient>(), new SensorPipes(time, logs.CreateLogger<SensorPipes>(), starts: false)), time),
            StartWithWindows.Of(ThermiPaths.Launcher),
            logs.CreateLogger("Thermi.Agents"));
    }

    private static bool Exists(string key)
    {
        using var found = Registry.LocalMachine.OpenSubKey(key);
        return found is not null;
    }
}
