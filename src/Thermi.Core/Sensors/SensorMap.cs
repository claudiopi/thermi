using System.Globalization;
using Thermi.Core.Frames;
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

    /// <summary>The end of the name the library gives an Intel core's distance to TjMax, after the core's own.</summary>
    public const string DistanceToTjMax = "Distance to TjMax";

    // The library's types that thermi reads as one of its own under another name.
    private static readonly Dictionary<string, SensorKind> LibraryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Level"] = SensorKind.Load,
        ["TimeSpan"] = SensorKind.Duration,
    };

    // OnlyPreferred: a "Data" sensor is any size (used, available, a stick's capacity), so a guess would be a wrong number;
    // a network's, a drive's and a battery's sensors of one type mean different things, so only the named one is the metric.
    // Device: which adapter or drive the metric follows, when a machine has several. SkipName: a sensor of the type that
    // measures something else. OnChip: the preferred names on a chip whose name starts with the key, in their place.
    private sealed record Rule(
        string[] HardwareTypes,
        string Type,
        string[] PreferredNames,
        string? SkipHardware = null,
        bool OnlyPreferred = false,
        Follow Device = Follow.Any,
        string? SkipName = null,
        (string Chip, string[] Names)? OnChip = null);

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
        // An Intel CPU reports its bus's clock first, at about 100 MHz: never a core's.
        [Metrics.CpuClock.Id] = new(["Cpu"], "Clock", ["Cores (Average)", "Core #1", "P-Core #1", "CPU Core #1"], SkipName: "Bus Speed"),
        [Metrics.GpuTemperature.Id] = new(Gpus, "Temperature", ["GPU Core"]),
        [Metrics.GpuLoad.Id] = new(Gpus, "Load", ["GPU Core"]),
        [Metrics.GpuPower.Id] = new(Gpus, "Power", ["GPU Package", "GPU Power"]),
        // Virtual memory reports a load named "Memory" too, and it comes first.
        [Metrics.RamLoad.Id] = new(["Memory"], "Load", ["Memory"], SkipHardware: "Virtual Memory"),
        [Metrics.RamUsed.Id] = new(["Memory"], "Data", ["Memory Used"], SkipHardware: "Virtual Memory", OnlyPreferred: true),
        // A laptop reports its fans on its embedded controller, with no board chip. Nuvoton's NCT67xx chips have their CPU
        // fan's input second, after a system fan's, and boards wire the CPU fan header to it.
        [Metrics.CpuFan.Id] = new(["SuperIO", "EmbeddedController"], "Fan", ["CPU Fan", "Fan #1"], OnChip: ("Nuvoton NCT67", ["CPU Fan", "Fan #2"])),
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
        [Metrics.Fps.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameRate), [GameSensors.Fps], OnlyPreferred: true),
        [Metrics.FrameTime.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameTime), [GameSensors.FrameTime], OnlyPreferred: true),
        [Metrics.OnePercentLow.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameRate), [GameSensors.OnePercentLow], OnlyPreferred: true),
        [Metrics.PointOnePercentLow.Id] = new([GameSensors.HardwareType], nameof(SensorKind.FrameRate), [GameSensors.PointOnePercentLow], OnlyPreferred: true),
    };

    /// <summary>
    /// Every metric's reading; the disk metrics follow <paramref name="drive"/>, the device id <see cref="BusiestDrive"/> gives,
    /// or the busiest drive now without one.
    /// </summary>
    public static IReadOnlyDictionary<string, Reading> Read(Snapshot snapshot, IReadOnlyDictionary<string, string> sources, string? drive = null)
    {
        var chosen = Chosen(snapshot, sources, drive);
        return Metrics.All.ToDictionary(
            metric => metric.Id,
            metric => chosen[metric.Id] is { } sensor && ReadingOf(sensor) is { } reading
                ? reading
                : new Reading(metric.Kind, null) { Direction = DirectionOf(metric) },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every metric, with the sensors an overlay's items and meters name in place of the automatic choice, and
    /// a reading under its own id for each sensor they name that the snapshot has, combined ones included.
    /// </summary>
    public static IReadOnlyDictionary<string, Reading> Read(Snapshot snapshot, OverlayConfig overlay, string? drive = null)
    {
        var readings = new Dictionary<string, Reading>(Read(snapshot, SourcesOf(overlay), drive), StringComparer.Ordinal);
        foreach (var id in overlay.Items.SelectMany(item => new[] { item.Sensor, item.WarnBy?.Sensor }.Concat(item.Sensors ?? [])).OfType<string>())
        {
            if (Find(snapshot, id) is { } sensor && ReadingOf(sensor) is { } reading)
            {
                readings.TryAdd(id, reading);
            }
        }

        return readings;
    }

    /// <summary>The sensor each metric reads for an overlay, by the metric's id, or null where none is found; the window names it beside "Auto".</summary>
    public static IReadOnlyDictionary<string, RawSensor?> Chosen(Snapshot snapshot, OverlayConfig overlay, string? drive = null) =>
        Chosen(snapshot, SourcesOf(overlay), drive);

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
    /// Whether a zero means "not read": without admin rights or the driver, a CPU's temperature, power, clocks and voltages
    /// read exactly zero, as no running CPU does. Every other zero is real: a memory chip's limits, a stopped fan, an idle GPU.
    /// </summary>
    public static bool ZeroIsMissing(RawSensor sensor) =>
        sensor is { Value: 0, HardwareType: "Cpu" }
            && sensor.Type is nameof(SensorKind.Temperature) or nameof(SensorKind.Power) or nameof(SensorKind.Clock) or nameof(SensorKind.Voltage);

    /// <summary>Whether the sensor is a distance to TjMax, which the library types as a temperature though it falls as the core heats.</summary>
    public static bool IsDistanceToTjMax(RawSensor sensor) =>
        sensor.Type == nameof(SensorKind.Temperature) && sensor.Name.EndsWith(DistanceToTjMax, StringComparison.Ordinal);

    /// <summary>Whether thermi's driver seems missing: a CPU temperature reads zero, and so the board's sensors may not be read either.</summary>
    public static bool DriverMayBeMissing(Snapshot snapshot) =>
        snapshot.Sensors.Any(sensor => sensor.Type == nameof(SensorKind.Temperature) && ZeroIsMissing(sensor));

    public static bool IsGpu(RawSensor sensor) => Gpus.Contains(sensor.HardwareType, StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="gpu"/> comes after <paramref name="than"/> in the GPU metrics' order of preference, as a graphics
    /// chip in a CPU after a card.
    /// </summary>
    public static bool GpuAfter(RawSensor gpu, RawSensor than) =>
        Array.IndexOf(Gpus, gpu.HardwareType) > Array.IndexOf(Gpus, than.HardwareType);

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
        GameSensors.HardwareType => DeviceKind.Game,
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
    /// The metric a sensor is one of: the one whose rule picks it by name, as the board's CPU fan input is the CPU fan's,
    /// else, on its kind of device, one whose rule takes any sensor of its type there; null when none does. A new item
    /// from the sensor is named and labeled like it.
    /// </summary>
    public static Metric? MetricOf(RawSensor sensor)
    {
        var device = DeviceOf(sensor);
        var alike = Metrics.All
            .Where(metric => Rules.TryGetValue(metric.Id, out var rule)
                && rule.HardwareTypes.Contains(sensor.HardwareType, StringComparer.Ordinal)
                && rule.Type == sensor.Type
                && sensor.Hardware != rule.SkipHardware)
            .ToList();
        return PickedBy(sensor)
            ?? alike.Find(metric => device is not null && Metrics.DeviceOf(metric) == device && !Rules[metric.Id].OnlyPreferred);
    }

    /// <summary>
    /// The metric whose rule picks the sensor by its name wherever it is found, so on any machine; a fan's duty named as
    /// the CPU fan is, the CPU fan's duty. Null when none does.
    /// </summary>
    public static Metric? PickedBy(RawSensor sensor) =>
        sensor.Type == nameof(SensorKind.Control)
            ? (PickedBy(sensor with { Type = nameof(SensorKind.Fan) }) == Metrics.CpuFan ? Metrics.CpuFanDuty : null)
            : Metrics.All.FirstOrDefault(metric => Rules.TryGetValue(metric.Id, out var rule)
                && Picks(rule, sensor)
                && Preference(rule, sensor) >= 0);

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
        // Grouped once, since every metric looks only at its own kinds of device, and the window and the overlay pick
        // every second.
        var byHardware = snapshot.Sensors.ToLookup(sensor => sensor.HardwareType, StringComparer.Ordinal);
        foreach (var metric in Metrics.All)
        {
            chosen[metric.Id] = sources.TryGetValue(metric.Id, out var id)
                ? Find(snapshot, id) is { } named && KindOf(named) == metric.Kind ? named : null
                : Pick(metric, snapshot, byHardware, chosen, devices);
        }

        return chosen;
    }

    // An item or what it warns by that names a sensor in place of its metric names it for every item of the overlay.
    private static Dictionary<string, string> SourcesOf(OverlayConfig overlay)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in overlay.Items)
        {
            if (item is { Auto: { } metric, Sensor: { } sensor })
            {
                sources.TryAdd(metric, sensor);
            }

            if (item.WarnBy is { Auto: { } warnMetric, Sensor: { } warnSensor })
            {
                sources.TryAdd(warnMetric, warnSensor);
            }
        }

        return sources;
    }

    private static RawSensor? Pick(
        Metric metric,
        Snapshot snapshot,
        ILookup<string, RawSensor> byHardware,
        Dictionary<string, RawSensor?> chosen,
        Dictionary<Follow, string?> devices)
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
            .SelectMany(type => byHardware[type].Where(sensor => Picks(rule, sensor) && (device is null || DeviceIdOf(sensor) == device)))
            .ToList();
        return candidates.Where(sensor => Preference(rule, sensor) >= 0).MinBy(sensor => Preference(rule, sensor))
            ?? (rule.OnlyPreferred ? null : candidates.FirstOrDefault());
    }

    private static bool Picks(Rule rule, RawSensor sensor) =>
        rule.HardwareTypes.Contains(sensor.HardwareType, StringComparer.Ordinal) && sensor.Type == rule.Type && sensor.Hardware != rule.SkipHardware
        && sensor.Name != rule.SkipName
        && !IsDistanceToTjMax(sensor)
        && (!rule.OnlyPreferred || Preference(rule, sensor) >= 0);

    // The place of the sensor's name among the rule's preferred names on its chip, or -1 when it is none of them.
    private static int Preference(Rule rule, RawSensor sensor) => Array.IndexOf(
        rule.OnChip is { } chip && sensor.Hardware.StartsWith(chip.Chip, StringComparison.Ordinal) ? chip.Names : rule.PreferredNames,
        sensor.Name);

    // The adapter that has moved the most data, as the library counts it: a physical one while any moves data, so a VPN
    // or a virtual switch, which carries the same traffic again, never wins; any one when none does.
    // In one pass, without grouping, since it runs with every pick: of equal ones, the first listed wins.
    private static string? BusiestAdapter(Snapshot snapshot)
    {
        var moved = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sensor in snapshot.Sensors)
        {
            if (sensor is { HardwareType: Network, Type: nameof(SensorKind.Data) })
            {
                var id = DeviceIdOf(sensor);
                moved[id] = moved.GetValueOrDefault(id) + (sensor.Value is { } value && double.IsFinite(value) ? value : 0);
            }
        }

        var virtuals = snapshot.VirtualAdapters ?? [];
        var physicalMoves = moved.Any(adapter => adapter.Value > 0 && !virtuals.Contains(adapter.Key, StringComparer.Ordinal));
        string? busiest = null;
        var most = double.NegativeInfinity;
        foreach (var (id, data) in moved)
        {
            if (data > most && !(physicalMoves && virtuals.Contains(id, StringComparer.Ordinal)))
            {
                (busiest, most) = (id, data);
            }
        }

        return busiest;
    }

    private static string? BusiestDriveNow(Snapshot snapshot) =>
        DriveActivity(snapshot).OrderByDescending(drive => drive.Value).Select(drive => drive.Key).FirstOrDefault();
}
