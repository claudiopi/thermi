using Thermi.Core.Frames;

namespace Thermi.Core.Sensors.Rules;

/// <summary>Which adapter or drive a metric follows, when a machine has several.</summary>
internal enum Follow
{
    Any,
    Adapter,
    Drive,
}

/// <summary>
/// How <see cref="SensorMap"/> picks a metric's sensor. OnlyPreferred: a "Data" sensor is any size (used, available, a
/// stick's capacity), so a guess would be a wrong number; a network's, a drive's and a battery's sensors of one type mean
/// different things, so only the named one is the metric. SkipName: a sensor of the type that measures something else.
/// OnChip: the preferred names on a chip whose name starts with the key, in their place.
/// </summary>
internal sealed record Rule(
    string[] HardwareTypes,
    string Type,
    string[] PreferredNames,
    string? SkipHardware = null,
    bool OnlyPreferred = false,
    Follow Device = Follow.Any,
    string? SkipName = null,
    (string Chip, string[] Names)? OnChip = null);

/// <summary>Each metric's rule, by the metric's id.</summary>
internal static class SensorRules
{
    // Hardware types in order of preference: a discrete GPU usually reports as Nvidia or AMD, and a Ryzen's integrated GPU
    // also reports as AMD, so Nvidia goes first and the config settles the rest. A Mac has its chip's alone.
    public static readonly string[] Gpus = ["GpuNvidia", "GpuAmd", "GpuIntel", AppleSilicon.GpuHardware];

    // Each rule's names are LibreHardwareMonitor's on Windows, then the Linux kernel's for the same: k10temp's Tdie and Tctl
    // on AMD, coretemp's Package id 0 on Intel, RAPL's package-0, amdgpu's edge and PPT, and Intel's xe's pkg; then the
    // Mac's, as AppleSilicon names them.
    public static readonly Dictionary<string, Rule> All = new(StringComparer.Ordinal)
    {
        [Metrics.CpuTemperature.Id] = new(
            ["Cpu"],
            "Temperature",
            ["Core (Tctl/Tdie)", "Core (Tctl)", "CPU Package", "Core Average", "Tdie", "Tctl", "Package id 0", AppleSilicon.HottestCore]),
        [Metrics.CpuLoad.Id] = new(["Cpu"], "Load", ["CPU Total"]),
        [Metrics.CpuPower.Id] = new(["Cpu"], "Power", ["Package", "CPU Package", "package-0", AppleSilicon.CpuPower]),
        // An Intel CPU reports its bus's clock first, at about 100 MHz: never a core's.
        [Metrics.CpuClock.Id] = new(["Cpu"], "Clock", ["Cores (Average)", "Core #1", "P-Core #1", "CPU Core #1"], SkipName: "Bus Speed"),
        [Metrics.GpuTemperature.Id] = new(Gpus, "Temperature", ["GPU Core", "edge", "pkg"]),
        [Metrics.GpuLoad.Id] = new(Gpus, "Load", ["GPU Core", AppleSilicon.GpuLoad]),
        [Metrics.GpuPower.Id] = new(Gpus, "Power", ["GPU Package", "GPU Power", "PPT"]),
        // Virtual memory reports a load named "Memory" too, and it comes first.
        [Metrics.RamLoad.Id] = new(["Memory"], "Load", ["Memory"], SkipHardware: "Virtual Memory"),
        [Metrics.RamUsed.Id] = new(["Memory"], "Data", ["Memory Used"], SkipHardware: "Virtual Memory", OnlyPreferred: true),
        // A laptop reports its fans on its embedded controller, with no board chip, as a Mac on its SMC. Nuvoton's NCT67xx
        // chips have their CPU fan's input second, after a system fan's, and boards wire the CPU fan header to it.
        [Metrics.CpuFan.Id] = new(["SuperIO", "EmbeddedController"], "Fan", ["CPU Fan", "Fan #1"], OnChip: ("Nuvoton NCT67", ["CPU Fan", "Fan #2"])),
        [Metrics.NetDownload.Id] = new([SensorMap.Network], "Throughput", ["Download Speed"], OnlyPreferred: true, Device: Follow.Adapter),
        [Metrics.NetUpload.Id] = new([SensorMap.Network], "Throughput", ["Upload Speed"], OnlyPreferred: true, Device: Follow.Adapter),
        [Metrics.DiskActivity.Id] = new([SensorMap.Storage], "Load", ["Total Activity"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.DiskRead.Id] = new([SensorMap.Storage], "Throughput", ["Read Rate"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.DiskWrite.Id] = new([SensorMap.Storage], "Throughput", ["Write Rate"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.BatteryCharge.Id] = new(["Battery"], "Level", ["Charge Level"], OnlyPreferred: true),
        [Metrics.BatteryRate.Id] = new(["Battery"], "Power", ["Charge/Discharge Rate", "Charge Rate", "Discharge Rate"], OnlyPreferred: true),
        [Metrics.BatteryTime.Id] = new(["Battery"], "TimeSpan", ["Remaining Time (Estimated)"], OnlyPreferred: true),
        [Metrics.Uptime.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Duration), [SystemSensors.Uptime], OnlyPreferred: true),
        [Metrics.LocalIp.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Address), [SystemSensors.LocalIp], OnlyPreferred: true),
        [Metrics.Clock.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Time), [SystemSensors.Clock], OnlyPreferred: true),
        [Metrics.Fps.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameRate), [GameSensors.Fps], OnlyPreferred: true),
        [Metrics.FrameTime.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameTime), [GameSensors.FrameTime], OnlyPreferred: true),
    };
}
