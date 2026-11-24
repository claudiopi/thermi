using Thermi.Core.Sensors;

namespace Thermi.Core.Settings;

/// <summary>How alarming a reading is.</summary>
public enum Level
{
    Normal,
    Warning,
    Critical,
}

/// <summary>A meter's lines, in the unit of what it reads: a warning, a critical, or both, the warning never above the critical.</summary>
public sealed record Threshold
{
    /// <summary>The highest line a fan's speed takes, in RPM; every other meter's lines run from 0 to 100 but a throughput's.</summary>
    public const double MaxFanSpeed = 10_000;

    /// <summary>The highest line a throughput takes, in bytes per second: 100 GB/s, past any drive or network a PC has.</summary>
    public const double MaxThroughput = 100e9;

    // A CPU's limit when nothing says otherwise: AMD's, which the defaults also take, having no machine to ask.
    private const double DefaultCpuLimit = 100;

    // Each line is optional in the file, where a meter that only warns has no critical.
    public Threshold(double? warning = null, double? critical = null)
    {
        if ((warning is null && critical is null) || !Finite(warning) || !Finite(critical) || warning > critical)
        {
            throw new ArgumentException($"Expected one or two finite values with warning <= critical, got {warning} and {critical}.", nameof(warning));
        }

        Warning = warning;
        Critical = critical;
    }

    public double? Warning { get; }

    public double? Critical { get; }

    /// <summary>Whether a meter can read that type: a temperature, a load, a duty, a fan's speed or a throughput, the values lines can judge.</summary>
    public static bool CanMeter(SensorKind kind) =>
        kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Control or SensorKind.Fan or SensorKind.Throughput;

    /// <summary>
    /// The meters an item of <paramref name="kind"/> is offered: readings of the item itself, never of another device or
    /// another kind. Its own metric or sensor when that type can be a meter, then its other form: a fan's own duty, or a
    /// duty's own fan, where the service reads it; a combined item only itself, and one whose type cannot be a meter none,
    /// but the RAM in use, judged by the RAM's load.
    /// </summary>
    public static IReadOnlyList<MeterConfig> MetersOf(ItemConfig item, SensorKind? kind, Snapshot known)
    {
        // The memory in use has no lines that suit every PC in GB, so it is judged as the share of the memory it is.
        if (item is { Sensors: null } && (item.Metric ?? item.Auto) == Metrics.RamUsed.Id)
        {
            return [new MeterConfig { Metric = Metrics.RamLoad.Id }];
        }

        if (kind is not { } type || !CanMeter(type))
        {
            return [];
        }

        MeterConfig? self = item.Metric is { } metric
            ? new MeterConfig { Metric = metric }
            : item.Sensor is { } sensor ? new MeterConfig { Sensor = sensor } : null;
        var other = (type, item.Metric) switch
        {
            _ when item.Sensors is not null => null,
            (SensorKind.Fan, { } fan) when fan == Metrics.CpuFan.Id => new MeterConfig { Metric = Metrics.CpuFanDuty.Id },
            (SensorKind.Control, { } duty) when duty == Metrics.CpuFanDuty.Id => new MeterConfig { Metric = Metrics.CpuFan.Id },
            (SensorKind.Fan, null) when item.Sensor is { } fan && SensorMap.OwnDuty(fan) is { } duty && SensorMap.Find(known, duty) is not null =>
                new MeterConfig { Sensor = duty, Auto = item.Auto == Metrics.CpuFan.Id ? Metrics.CpuFanDuty.Id : null },
            (SensorKind.Control, null) when item.Sensor is { } duty && SensorMap.OwnFan(duty) is { } fan && SensorMap.Find(known, fan) is not null =>
                new MeterConfig { Sensor = fan, Auto = item.Auto == Metrics.CpuFanDuty.Id ? Metrics.CpuFan.Id : null },
            _ => null,
        };
        return [.. new[] { self, other }.OfType<MeterConfig>()];
    }

    /// <summary>The highest value a line of that meter takes, in its unit.</summary>
    public static double MaxOf(SensorKind kind) =>
        kind == SensorKind.Fan ? MaxFanSpeed
        : kind == SensorKind.Throughput ? MaxThroughput
        : 100;

    /// <summary>The lines a metric's meter starts with on any machine, or null for none; a CPU's limit is taken as AMD's.</summary>
    public static Threshold? Default(Metric metric) => Default(metric.Kind, Metrics.DeviceOf(metric), DefaultCpuLimit);

    /// <summary>
    /// The lines a meter starts with on the machine the snapshot is from, or null for none: a CPU temperature warns 10 °C
    /// under the CPU's limit and is critical 5 under it, a GPU's at 80 and 88 °C, any other temperature at 70 and 80 °C, and a
    /// fan's duty warns at 85 %. Loads and fan speeds have none.
    /// </summary>
    public static Threshold? Default(MeterConfig meter, Snapshot snapshot)
    {
        var metric = Metrics.Find(meter.Metric ?? meter.Auto);
        var sensor = meter.Sensor is { } id ? SensorMap.Find(snapshot, id)
            : metric is not null ? SensorMap.Chosen(snapshot, new OverlayConfig { Items = [] })[metric.Id]
            : null;
        var kind = metric?.Kind ?? (sensor is null ? null : SensorMap.KindOf(sensor));
        var device = sensor is null ? (metric is null ? null : Metrics.DeviceOf(metric)) : SensorMap.DeviceOf(sensor);
        return kind is { } known ? Default(known, device, CpuLimit(sensor, snapshot)) : null;
    }

    /// <summary>
    /// A missing reading is never alarming: the overlay shows it as missing instead. A reading is judged as it shows,
    /// rounded to its type's decimals or to <paramref name="decimals"/>, so a value that reads as its line is at it.
    /// </summary>
    public Level LevelOf(Reading reading, int? decimals = null) => Shown(reading, decimals) switch
    {
        null => Level.Normal,
        var value when value >= Critical => Level.Critical,
        var value when value >= Warning => Level.Warning,
        _ => Level.Normal,
    };

    // Only the types that show in the unit their lines are kept in, a temperature in °C, round here as they show.
    private static double? Shown(Reading reading, int? decimals) =>
        reading.Usable is { } value && reading.Kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Control or SensorKind.Power or SensorKind.Fan
            ? Math.Round(value, decimals ?? 0, MidpointRounding.AwayFromZero)
            : reading.Usable;

    // A line warns above a value, and a battery's trouble is below one, which Windows warns of itself.
    private static Threshold? Default(SensorKind kind, DeviceKind? device, double cpuLimit) => (kind, device) switch
    {
        (_, DeviceKind.Battery) => null,
        (SensorKind.Temperature, DeviceKind.Cpu) => new Threshold(cpuLimit - 10, cpuLimit - 5),
        (SensorKind.Temperature, DeviceKind.Gpu) => new Threshold(80, 88),
        (SensorKind.Temperature, _) => new Threshold(70, 80),
        (SensorKind.Control, _) => new Threshold(85, null),
        _ => null,
    };

    // An Intel CPU reports how far each core is under its limit, TjMax, so the limit is a temperature plus its distance:
    // the sensor's own, or the CPU's first.
    private static double CpuLimit(RawSensor? sensor, Snapshot snapshot)
    {
        if (sensor is not { HardwareType: "Cpu", Value: { } value })
        {
            return DefaultCpuLimit;
        }

        var distances = snapshot.Sensors
            .Where(each => each.Hardware == sensor.Hardware && SensorMap.IsDistanceToTjMax(each) && each.Value is not null)
            .ToList();
        var distance = distances.FirstOrDefault(each => each.Name == $"{sensor.Name} {SensorMap.DistanceToTjMax}") ?? distances.FirstOrDefault();
        return distance?.Value is { } below ? Math.Round(value + below) : DefaultCpuLimit;
    }

    private static bool Finite(double? value) => value is not { } number || double.IsFinite(number);
}
