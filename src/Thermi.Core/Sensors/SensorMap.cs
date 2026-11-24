using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.Core.Sensors;

/// <summary>
/// Finds the sensor behind each metric. The config can name a sensor by its source id; otherwise a rule
/// picks it by hardware, type and preferred names, because source ids differ between machines and versions.
/// </summary>
public static class SensorMap
{
    // Hardware types in order of preference: a discrete GPU usually reports as Nvidia or AMD, and a
    // Ryzen's integrated GPU also reports as AMD, so Nvidia goes first and the config settles the rest.
    private static readonly string[] Gpus = ["GpuNvidia", "GpuAmd", "GpuIntel"];

    private const char RepeatMark = '#';

    private const string Network = "Network";

    private const string Storage = "Storage";

    // The library's types that thermi reads as one of its own under another name.
    private static readonly Dictionary<string, SensorKind> LibraryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Level"] = SensorKind.Load,
        ["TimeSpan"] = SensorKind.Duration,
    };

    // OnlyPreferred: a "Data" sensor is any size (used, available, a stick's capacity), so a guess would be a wrong number;
    // a network's, a drive's and a battery's sensors of one type mean different things, so only the named one is the metric.
    // Device: which adapter or drive the metric follows, when a machine has several.
    private sealed record Rule(string[] HardwareTypes, string Type, string[] PreferredNames, string? SkipHardware = null, bool OnlyPreferred = false, Follow Device = Follow.Any);

    private enum Follow
    {
        Any,
        Adapter,
        Drive,
    }

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
        [Metrics.NetDownload.Id] = new([Network], "Throughput", ["Download Speed"], OnlyPreferred: true, Device: Follow.Adapter),
        [Metrics.NetUpload.Id] = new([Network], "Throughput", ["Upload Speed"], OnlyPreferred: true, Device: Follow.Adapter),
        [Metrics.DiskActivity.Id] = new([Storage], "Load", ["Total Activity"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.DiskRead.Id] = new([Storage], "Throughput", ["Read Rate"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.DiskWrite.Id] = new([Storage], "Throughput", ["Write Rate"], OnlyPreferred: true, Device: Follow.Drive),
        [Metrics.BatteryCharge.Id] = new(["Battery"], "Level", ["Charge Level"], OnlyPreferred: true),
        [Metrics.BatteryRate.Id] = new(["Battery"], "Power", ["Charge/Discharge Rate", "Charge Rate", "Discharge Rate"], OnlyPreferred: true),
        [Metrics.BatteryTime.Id] = new(["Battery"], "TimeSpan", ["Remaining Time (Estimated)"], OnlyPreferred: true),
        [Metrics.Uptime.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Duration), [SystemSensors.Uptime], OnlyPreferred: true),
        [Metrics.LocalIp.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Address), [SystemSensors.LocalIp], OnlyPreferred: true),
        [Metrics.Clock.Id] = new([SystemSensors.HardwareType], nameof(SensorKind.Time), [SystemSensors.Clock], OnlyPreferred: true),
    };

    /// <summary>Every metric's reading; the disk metrics follow <paramref name="drive"/>, the device id <see cref="BusiestDrive"/> gives, or the busiest drive now without one.</summary>
    public static IReadOnlyDictionary<string, Reading> Read(Snapshot snapshot, IReadOnlyDictionary<string, string> sources, string? drive = null)
    {
        var chosen = Chosen(snapshot, sources, drive);
        return Metrics.All.ToDictionary(
            metric => metric.Id,
            metric => chosen[metric.Id] is { } sensor && ReadingOf(sensor) is { } reading ? reading : new Reading(metric.Kind, null) { Direction = DirectionOf(metric) },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every metric, with the sensors an overlay's items and meters name in place of the automatic choice, and
    /// a reading under its own id for each sensor they name that the snapshot has, combined ones included.
    /// </summary>
    public static IReadOnlyDictionary<string, Reading> Read(Snapshot snapshot, OverlayConfig overlay, string? drive = null)
    {
        var readings = new Dictionary<string, Reading>(Read(snapshot, SourcesOf(overlay), drive), StringComparer.Ordinal);
        foreach (var id in overlay.Items.SelectMany(item => new[] { item.Sensor, item.Meter?.Sensor }.Concat(item.Sensors ?? [])).OfType<string>())
        {
            if (Find(snapshot, id) is { } sensor && ReadingOf(sensor) is { } reading)
            {
                readings.TryAdd(id, reading);
            }
        }

        return readings;
    }

    /// <summary>The sensor each metric reads for an overlay, by the metric's id, or null where none is found; the window names it beside "Auto".</summary>
    public static IReadOnlyDictionary<string, RawSensor?> Chosen(Snapshot snapshot, OverlayConfig overlay, string? drive = null) => Chosen(snapshot, SourcesOf(overlay), drive);

    /// <summary>
    /// Every sensor under the id an item names it by. The library reports some sensors under an id it already gave
    /// another, as "/gpu-nvidia/0/load/3" for both "GPU Bus" and "GPU Memory": the first keeps the id, and each
    /// repeat is named by the id with its place among them after a '#', so no sensor is out of reach.
    /// </summary>
    public static IEnumerable<(string Id, RawSensor Sensor)> Named(Snapshot snapshot)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sensor in snapshot.Sensors)
        {
            var place = seen[sensor.Id] = seen.GetValueOrDefault(sensor.Id) + 1;
            yield return (place == 1 ? sensor.Id : string.Create(CultureInfo.InvariantCulture, $"{sensor.Id}{RepeatMark}{place}"), sensor);
        }
    }

    public static RawSensor? Find(Snapshot snapshot, string id)
    {
        var mark = id.LastIndexOf(RepeatMark);
        var (sensorId, place) = mark > 0 && int.TryParse(id.AsSpan(mark + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var repeat) && repeat > 1
            ? (id[..mark], repeat)
            : (id, 1);
        return snapshot.Sensors.Where(sensor => sensor.Id == sensorId).Skip(place - 1).FirstOrDefault();
    }

    /// <summary>
    /// What the sensor reads, with the zero rule applied, or null for a type thermi does not show. A network's speed knows
    /// which way it goes, and a battery's rate is below zero while it discharges.
    /// </summary>
    public static Reading? ReadingOf(RawSensor sensor) =>
        KindOf(sensor) is { } kind ? new Reading(kind, ZeroIsMissing(sensor) ? null : Signed(sensor)) { Direction = DirectionOf(sensor) } : null;

    /// <summary>
    /// Whether a zero means "not read". Without admin rights or the driver, the CPU's temperature, power and clocks
    /// read exactly zero instead of nothing, and no running CPU is at 0 °C, draws 0 W or runs at 0 MHz. Every other
    /// zero is real: a memory chip's limits, a stopped fan, an idle GPU.
    /// </summary>
    public static bool ZeroIsMissing(RawSensor sensor) =>
        sensor is { Value: 0, HardwareType: "Cpu", Type: nameof(SensorKind.Temperature) or nameof(SensorKind.Power) or nameof(SensorKind.Clock) };

    /// <summary>Whether thermi's driver seems missing: a CPU temperature reads zero, and so the board's sensors may not be read either.</summary>
    public static bool DriverMayBeMissing(Snapshot snapshot) =>
        snapshot.Sensors.Any(sensor => sensor.Type == nameof(SensorKind.Temperature) && ZeroIsMissing(sensor));

    public static bool IsGpu(RawSensor sensor) => Gpus.Contains(sensor.HardwareType, StringComparer.Ordinal);

    /// <summary>What the sensor measures, or null for a type thermi does not show.</summary>
    public static SensorKind? KindOf(RawSensor sensor) => KindOfType(sensor.Type);

    /// <summary>
    /// What a sensor measures, from its id alone, which the library makes from its device, its type and its index, as
    /// "/lpc/it8689e/0/fan/1": so a meter's type is known while the service is not read. Null for an id of another shape.
    /// </summary>
    public static SensorKind? KindOfId(string id)
    {
        var parts = id.Split(RepeatMark)[0].Split('/');
        return parts.Length >= 3 ? KindOfType(parts[^2]) : null;
    }

    /// <summary>The kind of device the sensor is on, or null for one thermi has no pill for.</summary>
    public static DeviceKind? DeviceOf(RawSensor sensor) => sensor.HardwareType switch
    {
        "Cpu" => DeviceKind.Cpu,
        "Memory" => DeviceKind.Ram,
        Storage => DeviceKind.Ssd,
        Network => DeviceKind.Network,
        "Battery" => DeviceKind.Battery,
        SystemSensors.HardwareType => DeviceKind.System,
        "Motherboard" or "SuperIO" or "EmbeddedController" => DeviceKind.Board,
        _ when IsGpu(sensor) => DeviceKind.Gpu,
        _ => null,
    };

    /// <summary>
    /// The library's id of the device a sensor is on. The library names a sensor by its device's id, its type and its
    /// index, as "/lpc/it8689e/0/fan/1"; names are not enough, since two GPUs of one model share theirs.
    /// </summary>
    public static string DeviceIdOf(RawSensor sensor)
    {
        var index = sensor.Id.LastIndexOf('/');
        var type = index > 0 ? sensor.Id.LastIndexOf('/', index - 1) : -1;
        return type > 0 ? sensor.Id[..type] : sensor.Hardware;
    }

    /// <summary>
    /// The metric a sensor is one of, on its kind of device: the one whose rule names it, else one whose rule takes any
    /// sensor of its type there; null when none does. A new item from the sensor is named and labeled like it.
    /// </summary>
    public static Metric? MetricOf(RawSensor sensor)
    {
        var device = DeviceOf(sensor);
        var alike = Metrics.All
            .Where(metric => device is not null && Metrics.DeviceOf(metric) == device && Rules.TryGetValue(metric.Id, out var rule) && rule.HardwareTypes.Contains(sensor.HardwareType, StringComparer.Ordinal) && rule.Type == sensor.Type)
            .ToList();
        return alike.Find(metric => Rules[metric.Id].PreferredNames.Contains(sensor.Name, StringComparer.Ordinal))
            ?? alike.Find(metric => !Rules[metric.Id].OnlyPreferred);
    }

    /// <summary>A fan's own duty is the control with the same index on the same chip: ".../fan/0" goes with ".../control/0".</summary>
    public static string? OwnDuty(string fanId)
    {
        var controlId = fanId.Replace("/fan/", "/control/", StringComparison.Ordinal);
        return controlId == fanId ? null : controlId;
    }

    /// <summary>A duty's own fan, the other way round from <see cref="OwnDuty"/>.</summary>
    public static string? OwnFan(string dutyId)
    {
        var fanId = dutyId.Replace("/control/", "/fan/", StringComparison.Ordinal);
        return fanId == dutyId ? null : fanId;
    }

    /// <summary>Each drive's total activity now, by its device id, for <see cref="BusiestDrive"/>.</summary>
    internal static Dictionary<string, double> DriveActivity(Snapshot snapshot)
    {
        var activity = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sensor in snapshot.Sensors.Where(sensor => Picks(Rules[Metrics.DiskActivity.Id], sensor)))
        {
            activity.TryAdd(DeviceIdOf(sensor), sensor.Value is { } value && double.IsFinite(value) ? value : 0);
        }

        return activity;
    }

    private static SensorKind? KindOfType(string type) =>
        LibraryTypes.TryGetValue(type, out var library) ? library
        : type.Length > 0 && type.All(char.IsAsciiLetter) && Enum.TryParse<SensorKind>(type, ignoreCase: true, out var kind) ? kind
        : null;

    private static Direction DirectionOf(RawSensor sensor) => sensor is { HardwareType: Network, Type: "Throughput" }
        ? sensor.Name switch
        {
            "Download Speed" => Direction.Down,
            "Upload Speed" => Direction.Up,
            _ => Direction.None,
        }
        : Direction.None;

    private static Direction DirectionOf(Metric metric) =>
        metric == Metrics.NetDownload ? Direction.Down : metric == Metrics.NetUpload ? Direction.Up : Direction.None;

    // The library names a battery's rate by which way the charge goes; thermi shows one rate, below zero while it discharges.
    private static double? Signed(RawSensor sensor) =>
        sensor is { HardwareType: "Battery", Name: "Discharge Rate", Value: { } value } ? -Math.Abs(value) : sensor.Value;

    private static Dictionary<string, RawSensor?> Chosen(Snapshot snapshot, IReadOnlyDictionary<string, string> sources, string? drive)
    {
        var chosen = new Dictionary<string, RawSensor?>(StringComparer.Ordinal);
        var devices = new Dictionary<Follow, string?>
        {
            [Follow.Adapter] = BusiestAdapter(snapshot),
            [Follow.Drive] = drive ?? BusiestDriveNow(snapshot),
        };
        foreach (var metric in Metrics.All)
        {
            chosen[metric.Id] = sources.TryGetValue(metric.Id, out var id)
                ? Find(snapshot, id) is { } named && KindOf(named) == metric.Kind ? named : null
                : Pick(metric, snapshot, chosen, devices);
        }

        return chosen;
    }

    // An item or a meter that names a sensor in place of its metric names it for every item of the overlay.
    private static Dictionary<string, string> SourcesOf(OverlayConfig overlay)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in overlay.Items)
        {
            if (item is { Auto: { } metric, Sensor: { } sensor })
            {
                sources.TryAdd(metric, sensor);
            }

            if (item.Meter is { Auto: { } meterMetric, Sensor: { } meterSensor })
            {
                sources.TryAdd(meterMetric, meterSensor);
            }
        }

        return sources;
    }

    private static RawSensor? Pick(Metric metric, Snapshot snapshot, Dictionary<string, RawSensor?> chosen, Dictionary<Follow, string?> devices)
    {
        if (metric == Metrics.CpuFanDuty)
        {
            return chosen[Metrics.CpuFan.Id]?.Id is { } fanId && OwnDuty(fanId) is { } controlId
                ? Find(snapshot, controlId)
                : null;
        }

        var rule = Rules[metric.Id];
        var device = devices.GetValueOrDefault(rule.Device);
        var candidates = rule.HardwareTypes
            .SelectMany(type => snapshot.Sensors.Where(sensor => sensor.HardwareType == type && Picks(rule, sensor) && (device is null || DeviceIdOf(sensor) == device)))
            .ToList();
        return rule.PreferredNames.Select(name => candidates.FirstOrDefault(sensor => sensor.Name == name)).FirstOrDefault(sensor => sensor is not null)
            ?? (rule.OnlyPreferred ? null : candidates.FirstOrDefault());
    }

    private static bool Picks(Rule rule, RawSensor sensor) =>
        rule.HardwareTypes.Contains(sensor.HardwareType, StringComparer.Ordinal) && sensor.Type == rule.Type && sensor.Hardware != rule.SkipHardware
        && (!rule.OnlyPreferred || rule.PreferredNames.Contains(sensor.Name, StringComparer.Ordinal));

    // The adapter that has moved the most data, as the library counts it: a physical one while any moves data, so a VPN
    // or a virtual switch, which carries the same traffic again, never wins; any one when none does.
    private static string? BusiestAdapter(Snapshot snapshot)
    {
        var moved = snapshot.Sensors
            .Where(sensor => sensor is { HardwareType: Network, Type: nameof(SensorKind.Data) })
            .GroupBy(DeviceIdOf, StringComparer.Ordinal)
            .Select(adapter => (Id: adapter.Key, Data: adapter.Sum(sensor => sensor.Value is { } value && double.IsFinite(value) ? value : 0)))
            .ToList();
        var physical = moved.Where(adapter => !(snapshot.VirtualAdapters ?? []).Contains(adapter.Id, StringComparer.Ordinal)).ToList();
        var pool = physical.Exists(adapter => adapter.Data > 0) ? physical : moved;
        return pool.OrderByDescending(adapter => adapter.Data).Select(adapter => adapter.Id).FirstOrDefault();
    }

    private static string? BusiestDriveNow(Snapshot snapshot) =>
        DriveActivity(snapshot).OrderByDescending(drive => drive.Value).Select(drive => drive.Key).FirstOrDefault();
}
