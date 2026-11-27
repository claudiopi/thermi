using Thermi.Core.Translations;

namespace Thermi.Core.Sensors;

/// <summary>
/// A value the overlay can show. <see cref="Id"/> never changes between thermi versions: configs name it.
/// <see cref="Name"/> is what the window calls it.
/// </summary>
public sealed record Metric(string Id, MetricLabel Label, SensorKind Kind, MetricName Name);

/// <summary>The label a metric's item shows when it has none of its own, in the language of the translations.</summary>
[Translated]
public enum MetricLabel
{
    Cpu,
    Gpu,
    Ram,
    Net,
    Ssd,
    Bat,
    Up,
    Ip,
    Time,
    Fps,
}

/// <summary>The kind of device a sensor is on, whose pill a sensor added as an item joins, named by its label.</summary>
[Translated]
public enum DeviceKind
{
    Cpu,
    Gpu,
    Ram,
    Board,
    Ssd,
    Network,
    Battery,

    /// <summary>Windows itself, whose uptime, address and clock thermi reads without a sensor.</summary>
    System,

    /// <summary>The app in front, whose frames the sensor service counts.</summary>
    Game,
}

/// <summary>What the window calls a metric, as the title of an item made from it, in the language of the translations.</summary>
[Translated]
public enum MetricName
{
    CpuTemperature,
    CpuLoad,
    CpuPower,
    CpuClock,
    GpuTemperature,
    GpuLoad,
    GpuPower,
    RamLoad,
    RamUsed,
    CpuFan,
    CpuFanDuty,
    NetDownload,
    NetUpload,
    DiskActivity,
    DiskRead,
    DiskWrite,
    BatteryCharge,
    BatteryRate,
    BatteryTime,
    Uptime,
    LocalIp,
    Clock,
    Fps,
    FrameTime,
    OnePercentLow,
    PointOnePercentLow,
}

public static class Metrics
{
    public static readonly Metric CpuTemperature = new("cpu.temperature", MetricLabel.Cpu, SensorKind.Temperature, MetricName.CpuTemperature);
    public static readonly Metric CpuLoad = new("cpu.load", MetricLabel.Cpu, SensorKind.Load, MetricName.CpuLoad);
    public static readonly Metric CpuPower = new("cpu.power", MetricLabel.Cpu, SensorKind.Power, MetricName.CpuPower);
    public static readonly Metric CpuClock = new("cpu.clock", MetricLabel.Cpu, SensorKind.Clock, MetricName.CpuClock);
    public static readonly Metric GpuTemperature = new("gpu.temperature", MetricLabel.Gpu, SensorKind.Temperature, MetricName.GpuTemperature);
    public static readonly Metric GpuLoad = new("gpu.load", MetricLabel.Gpu, SensorKind.Load, MetricName.GpuLoad);
    public static readonly Metric GpuPower = new("gpu.power", MetricLabel.Gpu, SensorKind.Power, MetricName.GpuPower);
    public static readonly Metric RamLoad = new("ram.load", MetricLabel.Ram, SensorKind.Load, MetricName.RamLoad);
    public static readonly Metric RamUsed = new("ram.used", MetricLabel.Ram, SensorKind.Data, MetricName.RamUsed);
    public static readonly Metric CpuFan = new("fan.cpu", MetricLabel.Cpu, SensorKind.Fan, MetricName.CpuFan);
    public static readonly Metric CpuFanDuty = new("fan.cpu.duty", MetricLabel.Cpu, SensorKind.Control, MetricName.CpuFanDuty);
    public static readonly Metric NetDownload = new("net.download", MetricLabel.Net, SensorKind.Throughput, MetricName.NetDownload);
    public static readonly Metric NetUpload = new("net.upload", MetricLabel.Net, SensorKind.Throughput, MetricName.NetUpload);
    public static readonly Metric DiskActivity = new("disk.activity", MetricLabel.Ssd, SensorKind.Load, MetricName.DiskActivity);
    public static readonly Metric DiskRead = new("disk.read", MetricLabel.Ssd, SensorKind.Throughput, MetricName.DiskRead);
    public static readonly Metric DiskWrite = new("disk.write", MetricLabel.Ssd, SensorKind.Throughput, MetricName.DiskWrite);
    public static readonly Metric BatteryCharge = new("battery.charge", MetricLabel.Bat, SensorKind.Load, MetricName.BatteryCharge);
    public static readonly Metric BatteryRate = new("battery.rate", MetricLabel.Bat, SensorKind.Power, MetricName.BatteryRate);
    public static readonly Metric BatteryTime = new("battery.time", MetricLabel.Bat, SensorKind.Duration, MetricName.BatteryTime);
    public static readonly Metric Uptime = new("system.uptime", MetricLabel.Up, SensorKind.Duration, MetricName.Uptime);
    public static readonly Metric LocalIp = new("system.ip", MetricLabel.Ip, SensorKind.Address, MetricName.LocalIp);
    public static readonly Metric Clock = new("system.clock", MetricLabel.Time, SensorKind.Time, MetricName.Clock);
    public static readonly Metric Fps = new("game.fps", MetricLabel.Fps, SensorKind.FrameRate, MetricName.Fps);
    public static readonly Metric FrameTime = new("game.frametime", MetricLabel.Fps, SensorKind.FrameTime, MetricName.FrameTime);
    public static readonly Metric OnePercentLow = new("game.low1", MetricLabel.Fps, SensorKind.FrameRate, MetricName.OnePercentLow);
    public static readonly Metric PointOnePercentLow = new("game.low01", MetricLabel.Fps, SensorKind.FrameRate, MetricName.PointOnePercentLow);

    public static IReadOnlyList<Metric> All { get; } =
    [
        CpuTemperature, CpuLoad, CpuPower, CpuClock, GpuTemperature, GpuLoad, GpuPower, RamLoad, RamUsed, CpuFan, CpuFanDuty,
        NetDownload, NetUpload, DiskActivity, DiskRead, DiskWrite, BatteryCharge, BatteryRate, BatteryTime, Uptime, LocalIp, Clock, Fps, FrameTime,
        OnePercentLow, PointOnePercentLow,
    ];

    public static Metric? Find(string? id) => All.FirstOrDefault(metric => metric.Id == id);

    /// <summary>The device a metric reads, whose pill its item joins; the CPU fan's is the CPU it cools, though the board reports it.</summary>
    public static DeviceKind? DeviceOf(Metric metric) => metric.Label switch
    {
        MetricLabel.Cpu => DeviceKind.Cpu,
        MetricLabel.Gpu => DeviceKind.Gpu,
        MetricLabel.Ram => DeviceKind.Ram,
        MetricLabel.Net => DeviceKind.Network,
        MetricLabel.Ssd => DeviceKind.Ssd,
        MetricLabel.Bat => DeviceKind.Battery,
        MetricLabel.Up or MetricLabel.Ip or MetricLabel.Time => DeviceKind.System,
        MetricLabel.Fps => DeviceKind.Game,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric.Label, "Unknown metric label."),
    };
}
