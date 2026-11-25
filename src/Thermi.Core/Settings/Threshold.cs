using Thermi.Core.Sensors;

namespace Thermi.Core.Settings;

/// <summary>How alarming a reading is.</summary>
public enum Level
{
    Normal,
    Warning,
    Critical,
}

/// <summary>
/// An item's lines, in the unit of what it warns by: a warning, a critical, or both. The warning is never past the
/// critical: below it, or above it for a reading whose trouble is a low value (<see cref="WarnsLow"/>), which the file's
/// rules check, since only they know what the item reads.
/// </summary>
public sealed record Threshold
{
    /// <summary>The highest line a fan's speed takes, in RPM; every other meter's lines run from 0 to 100 but a throughput's and a game's frames'.</summary>
    public const double MaxFanSpeed = 10_000;

    /// <summary>The highest line a throughput takes, in bytes per second: 100 GB/s, past any drive or network a PC has.</summary>
    public const double MaxThroughput = 100e9;

    /// <summary>The highest line a frame rate or a frame time takes, in frames per second or milliseconds.</summary>
    public const double MaxFrames = 1000;

    // A CPU's limit when nothing says otherwise: AMD's, which the defaults also take, having no machine to ask.
    private const double DefaultCpuLimit = 100;

    // Each line is optional in the file, where a meter that only warns has no critical.
    public Threshold(double? warning = null, double? critical = null)
    {
        if ((warning is null && critical is null) || !Finite(warning) || !Finite(critical))
        {
            throw new ArgumentException($"Expected one or two finite values, got {warning} and {critical}.", nameof(warning));
        }

        Warning = warning;
        Critical = critical;
    }

    public double? Warning { get; }

    public double? Critical { get; }

    /// <summary>Whether lines can judge that type: a temperature, a load, a duty, a fan's speed, a throughput or frames.</summary>
    public static bool CanMeter(SensorKind kind) =>
        kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Control or SensorKind.Fan or SensorKind.Throughput
            or SensorKind.FrameRate or SensorKind.FrameTime;

    /// <summary>Whether a reading of that type is in trouble when low, as a frame rate, so its lines warn below a value.</summary>
    public static bool WarnsLow(SensorKind kind) => kind == SensorKind.FrameRate;

    /// <summary>Whether the lines are in order for that type: the warning not past the critical.</summary>
    public bool InOrderFor(SensorKind kind) =>
        Warning is not { } warning || Critical is not { } critical || (WarnsLow(kind) ? warning >= critical : warning <= critical);

    /// <summary>
    /// What else than its own reading an item of <paramref name="kind"/> can warn by: the item itself in its other form,
    /// never another device or another kind. A fan's own duty, or a duty's own fan, where the service reads it, and the RAM
    /// in use its load; a combined item nothing else.
    /// </summary>
    public static IReadOnlyList<WarnByConfig> WarnByOf(ItemConfig item, SensorKind? kind, Snapshot known)
    {
        // The memory in use has no lines that suit every PC in GB, so it is judged as the share of the memory it is.
        if (item is { Sensors: null } && (item.Metric ?? item.Auto) == Metrics.RamUsed.Id)
        {
            return [new WarnByConfig { Metric = Metrics.RamLoad.Id }];
        }

        var other = (kind, item.Metric) switch
        {
            _ when item.Sensors is not null => null,
            (SensorKind.Fan, { } fan) when fan == Metrics.CpuFan.Id => new WarnByConfig { Metric = Metrics.CpuFanDuty.Id },
            (SensorKind.Control, { } duty) when duty == Metrics.CpuFanDuty.Id => new WarnByConfig { Metric = Metrics.CpuFan.Id },
            (SensorKind.Fan, null) when item.Sensor is { } fan && SensorMap.OwnDuty(fan) is { } duty && SensorMap.Find(known, duty) is not null =>
                new WarnByConfig { Sensor = duty, Auto = item.Auto == Metrics.CpuFan.Id ? Metrics.CpuFanDuty.Id : null },
            (SensorKind.Control, null) when item.Sensor is { } duty && SensorMap.OwnFan(duty) is { } fan && SensorMap.Find(known, fan) is not null =>
                new WarnByConfig { Sensor = fan, Auto = item.Auto == Metrics.CpuFanDuty.Id ? Metrics.CpuFan.Id : null },
            _ => null,
        };
        return other is null ? [] : [other];
    }

    /// <summary>What an item reads, from its metric or its first sensor's id, so it is known while the service is not read.</summary>
    public static SensorKind? KindOf(ItemConfig item) => KindOf(item.Metric ?? item.Auto, item.Sensor ?? (item.Sensors is [var first, ..] ? first : null));

    /// <summary>What a reading to warn by reads, from the file alone, as <see cref="KindOf(ItemConfig)"/>.</summary>
    public static SensorKind? KindOf(WarnByConfig warnBy) => KindOf(warnBy.Metric ?? warnBy.Auto, warnBy.Sensor);

    /// <summary>
    /// The type of the reading an item's lines judge, what it warns by or else its own, from the file alone; null when lines
    /// can judge no such reading, as power or a clock, so it never shows a level and draws nothing beside its value.
    /// </summary>
    public static SensorKind? JudgedKind(ItemConfig item) =>
        (item.WarnBy is { } other ? KindOf(other) : KindOf(item)) is { } kind && CanMeter(kind) ? kind : null;

    /// <summary>Whether lines can judge the item, as <see cref="JudgedKind"/> says.</summary>
    public static bool Judges(ItemConfig item) => JudgedKind(item) is not null;

    /// <summary>Whether a reading to warn by is the item's own, its metric or its sensor.</summary>
    public static bool IsOwn(ItemConfig item, WarnByConfig reading) =>
        (reading.Metric is not null && reading.Metric == item.Metric) || (reading.Sensor is not null && reading.Sensor == item.Sensor);

    /// <summary>The item's own reading as one to warn by: its metric, or its first sensor with the metric it keeps.</summary>
    public static WarnByConfig Own(ItemConfig item) =>
        item.Metric is { } metric ? new WarnByConfig { Metric = metric }
        : new WarnByConfig { Sensor = item.Sensor ?? (item.Sensors is [var first, ..] ? first : null), Auto = item.Auto };

    /// <summary>The highest value a line of a reading of that type takes, in its unit.</summary>
    public static double MaxOf(SensorKind kind) =>
        kind == SensorKind.Fan ? MaxFanSpeed
        : kind == SensorKind.Throughput ? MaxThroughput
        : kind is SensorKind.FrameRate or SensorKind.FrameTime ? MaxFrames
        : 100;

    /// <summary>The lines a metric starts with on any machine, or null for none; a CPU's limit is taken as AMD's.</summary>
    public static Threshold? Default(Metric metric) => Default(metric.Kind, Metrics.DeviceOf(metric), DefaultCpuLimit);

    /// <summary>The lines an item starts with on the machine the snapshot is from, those of what it warns by, or null for none.</summary>
    public static Threshold? Default(ItemConfig item, Snapshot snapshot) => Judges(item) ? Default(item.WarnBy ?? Own(item), snapshot) : null;

    /// <summary>
    /// The lines a reading starts with on the machine the snapshot is from, or null for none: a CPU temperature warns 10 °C
    /// under the CPU's limit and is critical 5 under it, a GPU's at 80 and 88 °C, any other temperature at 70 and 80 °C, and a
    /// fan's duty warns at 85 %. Loads, fan speeds and frames have none: a game's FPS is fine at 40 in one and poor in another.
    /// </summary>
    public static Threshold? Default(WarnByConfig meter, Snapshot snapshot)
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
        var value when WarnsLow(reading.Kind) => value <= Critical ? Level.Critical : value <= Warning ? Level.Warning : Level.Normal,
        var value when value >= Critical => Level.Critical,
        var value when value >= Warning => Level.Warning,
        _ => Level.Normal,
    };

    // Only the types that show in the unit their lines are kept in, a temperature in °C, round here as they show.
    private static double? Shown(Reading reading, int? decimals) =>
        reading.Usable is { } value && reading.Kind is SensorKind.Temperature or SensorKind.Load or SensorKind.Control or SensorKind.Power or SensorKind.Fan
            or SensorKind.FrameRate or SensorKind.FrameTime
            ? Math.Round(value, decimals ?? (reading.Kind == SensorKind.FrameTime ? 1 : 0), MidpointRounding.AwayFromZero)
            : reading.Usable;

    // A line warns above a value but a frame rate's, and a battery's trouble is below one, which Windows warns of itself.
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

    private static SensorKind? KindOf(string? metric, string? sensor) => Metrics.Find(metric)?.Kind ?? (sensor is null ? null : SensorMap.KindOfId(sensor));
}
