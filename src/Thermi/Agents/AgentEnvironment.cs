using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Thermi.Commands;
using Thermi.Core.Placement;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Windows;

namespace Thermi.Agents;

/// <summary>
/// What an agent's command reaches outside its process, which tests replace: the settings file, the sensors, read from the
/// pipe as the overlay reads them with a new connection for each read, whether the service is installed, whether an overlay
/// runs, whether this install is the sensors alone, the start at sign-in, the monitors, the keys AltGr types with, and the
/// fan finder's load.
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
    public Func<bool> Overlay { get; init; } = () => Mutex.TryOpenExisting(HostEnvironment.OverlayMutexName, out var found) && Released(found);

    public Func<bool> SensorsOnly { get; init; } = () =>
        Registry.LocalMachine.OpenSubKey(InstallKey)?.GetValue(SensorsOnlyValue) is 1;

    public Func<IReadOnlyList<Screen>> Screens { get; init; } = Desktop.Screens;

    public Func<IReadOnlySet<int>> AltGrKeys { get; init; } = HotkeyRegistry.AltGrKeys;

    public Action<CancellationToken> Load { get; init; } = CpuLoad.Run;

    public string StylesFolder => ConfigStore.StylesFolder(SettingsPath);

    public static AgentEnvironment Of(ILoggerFactory logs, TimeProvider time)
    {
        // The overlay starts from thermi.exe beside this door, whichever of the two ran.
        var overlay = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "thermi.exe");
        return new(
            ConfigStore.DefaultPath,
            () => new SystemFeed(new SensorClient(time, logs.CreateLogger<SensorClient>()), time),
            StartWithWindows.Of(overlay),
            logs.CreateLogger("Thermi.Agents"));
    }

    private static bool Exists(string key)
    {
        using var found = Registry.LocalMachine.OpenSubKey(key);
        return found is not null;
    }

    private static bool Released(Mutex mutex)
    {
        mutex.Dispose();
        return true;
    }
}
