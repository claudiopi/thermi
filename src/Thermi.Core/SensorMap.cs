namespace Thermi.Core;

/// <summary>
/// Finds the sensor behind each metric. The config can name a sensor by its source id; otherwise a rule
/// picks it by hardware, type and preferred names, because source ids differ between machines and versions.
/// </summary>
public static class SensorMap
{
    // Hardware types in order of preference: a discrete GPU usually reports as Nvidia or AMD, and a
    // Ryzen's integrated GPU also reports as AMD, so Nvidia goes first and the config settles the rest.
    private static readonly string[] Gpus = ["GpuNvidia", "GpuAmd", "GpuIntel"];

    // OnlyPreferred: a "Data" sensor is any size (used, available, a stick's capacity), so a guess would be a wrong number.
    private sealed record Rule(string[] HardwareTypes, string Type, string[] PreferredNames, string? SkipHardware = null, bool OnlyPreferred = false);

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.Ordinal)
    {
        [Metrics.CpuTemperature.Id] = new(["Cpu"], "Temperature", ["Core (Tctl/Tdie)", "Core (Tctl)", "CPU Package", "Core Average"]),
        [Metrics.CpuLoad.Id] = new(["Cpu"], "Load", ["CPU Total"]),
        [Metrics.CpuPower.Id] = new(["Cpu"], "Power", ["Package", "CPU Package"]),
        [Metrics.CpuClock.Id] = new(["Cpu"], "Clock", ["Cores (Average)", "Core #1"]),
        [Metrics.GpuTemperature.Id] = new(Gpus, "Temperature", ["GPU Core"]),
        [Metrics.GpuLoad.Id] = new(Gpus, "Load", ["GPU Core"]),
        [Metrics.GpuPower.Id] = new(Gpus, "Power", ["GPU Package", "GPU Power"]),
        // Virtual memory reports a load named "Memory" too, and it comes first.
        [Metrics.RamLoad.Id] = new(["Memory"], "Load", ["Memory"], SkipHardware: "Virtual Memory"),
        [Metrics.RamUsed.Id] = new(["Memory"], "Data", ["Memory Used"], SkipHardware: "Virtual Memory", OnlyPreferred: true),
        [Metrics.CpuFan.Id] = new(["SuperIO"], "Fan", ["CPU Fan", "Fan #1"]),
    };

    public static IReadOnlyDictionary<string, Reading> Read(Snapshot snapshot, IReadOnlyDictionary<string, string> sources)
    {
        var chosen = new Dictionary<string, RawSensor?>(StringComparer.Ordinal);
        foreach (var metric in Metrics.All)
        {
            chosen[metric.Id] = sources.TryGetValue(metric.Id, out var id)
                ? snapshot.Sensors.FirstOrDefault(sensor => sensor.Id == id && sensor.Type == metric.Kind.ToString())
                : Pick(metric, snapshot, chosen);
        }

        return Metrics.All.ToDictionary(metric => metric.Id, metric => new Reading(metric.Kind, Value(metric, chosen[metric.Id])), StringComparer.Ordinal);
    }

    // Without admin rights or the driver, the CPU's temperature and power read exactly zero instead of
    // nothing. No running chip is at 0 °C or draws 0 W, so zero means "not read" for those kinds.
    private static double? Value(Metric metric, RawSensor? sensor) =>
        sensor?.Value is 0 && metric.Kind is SensorKind.Temperature or SensorKind.Power ? null : sensor?.Value;

    private static RawSensor? Pick(Metric metric, Snapshot snapshot, Dictionary<string, RawSensor?> chosen)
    {
        // A fan's duty is the control with the same index on the same chip: ".../fan/0" goes with ".../control/0".
        if (metric == Metrics.CpuFanDuty)
        {
            var fanId = chosen[Metrics.CpuFan.Id]?.Id;
            var controlId = fanId?.Replace("/fan/", "/control/", StringComparison.Ordinal);
            return controlId == fanId ? null : snapshot.Sensors.FirstOrDefault(sensor => sensor.Id == controlId);
        }

        var rule = Rules[metric.Id];
        var candidates = rule.HardwareTypes
            .SelectMany(type => snapshot.Sensors.Where(sensor => sensor.HardwareType == type && sensor.Type == rule.Type && sensor.Hardware != rule.SkipHardware))
            .ToList();
        return rule.PreferredNames.Select(name => candidates.FirstOrDefault(sensor => sensor.Name == name)).FirstOrDefault(sensor => sensor is not null)
            ?? (rule.OnlyPreferred ? null : candidates.FirstOrDefault());
    }
}
