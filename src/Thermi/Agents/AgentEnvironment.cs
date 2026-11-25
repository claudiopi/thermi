using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Thermi.Commands;
using Thermi.Core.Sensors;
using Thermi.Logging;
using Thermi.Core.Placement;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Windows;

namespace Thermi.Agents;

/// <summary>
/// What an agent's command reaches outside its process, which tests replace: the settings file, the sensors, read from the
/// pipe as the overlay reads them with a new connection for each read, whether the service is installed, whether an overlay
/// runs, whether this install is the sensors alone, the start at sign-in, the monitors, the keys AltGr types with, and the
/// fan finder's load and the file that says when it last ran.
/// </summary>
internal sealed record AgentEnvironment(string SettingsPath, Func<ISensorFeed> Sensors, StartWithWindows Startup, ILogger Log)
{
    // The services' key, which every user may read; the service's own key there is its registration.
    private const string Services = @"SYSTEM\CurrentControlSet\Services\";

    // Written by the installer: 1 when it installed the sensors alone.
    private const string InstallKey = @"SOFTWARE\thermi";

    public const string SensorsOnlyValue = "SensorsOnly";

    public Func<bool> Installed { get; init; } = () => Exists(Services + StartSensors.Service);

    /// <summary>Whether an overlay runs in this session: its single-instance mutex is there, which is looked at, never taken.</summary>
    public Func<bool> Overlay { get; init; } = () => HostEnvironment.MutexExists(HostEnvironment.OverlayMutexName);

    public Func<bool> SensorsOnly { get; init; } = () =>
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstallKey);
        return key?.GetValue(SensorsOnlyValue) is 1;
    };

    public Func<IReadOnlyList<Screen>> Screens { get; init; } = Desktop.Screens;

    public Func<IReadOnlySet<int>> AltGrKeys { get; init; } = HotkeyRegistry.AltGrKeys;

    // The window's load: a thermi cpu-load of its own, at idle priority, so the PC stays as it was for the person using it.
    public Action<CancellationToken> Load { get; init; } =
        stop => CpuLoad.Start(BesideThisDoor("thermi.exe"), (int)FanFinder.MaxRun.TotalSeconds, stop);

    public string FinderRan { get; init; } = Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    public string StylesFolder => ConfigStore.StylesFolder(SettingsPath);

    public static AgentEnvironment Of(ILoggerFactory logs, TimeProvider time)
    {
        return new(
            ConfigStore.DefaultPath,
            () => new SystemFeed(new SensorClient(time, logs.CreateLogger<SensorClient>()), time),
            StartWithWindows.Of(BesideThisDoor("thermi.exe")),
            logs.CreateLogger("Thermi.Agents"));
    }

    // The overlay and the load start from thermi.exe beside this door, whichever of the two ran.
    private static string BesideThisDoor(string exe) =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, exe);

    private static bool Exists(string key)
    {
        using var found = Registry.LocalMachine.OpenSubKey(key);
        return found is not null;
    }
}
