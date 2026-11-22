namespace Thermi.Core;

/// <summary>A value the overlay can show. <see cref="Id"/> never changes between thermi versions: configs name it.</summary>
public sealed record Metric(string Id, string Label, SensorKind Kind);

public static class Metrics
{
    public static readonly Metric CpuTemperature = new("cpu.temperature", "CPU", SensorKind.Temperature);
    public static readonly Metric CpuLoad = new("cpu.load", "CPU", SensorKind.Load);
    public static readonly Metric CpuPower = new("cpu.power", "CPU", SensorKind.Power);
    public static readonly Metric CpuClock = new("cpu.clock", "CPU", SensorKind.Clock);
    public static readonly Metric GpuTemperature = new("gpu.temperature", "GPU", SensorKind.Temperature);
    public static readonly Metric GpuLoad = new("gpu.load", "GPU", SensorKind.Load);
    public static readonly Metric GpuPower = new("gpu.power", "GPU", SensorKind.Power);
    public static readonly Metric RamLoad = new("ram.load", "RAM", SensorKind.Load);
    public static readonly Metric RamUsed = new("ram.used", "RAM", SensorKind.Data);
    public static readonly Metric CpuFan = new("fan.cpu", "FAN", SensorKind.Fan);
    public static readonly Metric CpuFanDuty = new("fan.cpu.duty", "FAN", SensorKind.Control);

    public static IReadOnlyList<Metric> All { get; } =
        [CpuTemperature, CpuLoad, CpuPower, CpuClock, GpuTemperature, GpuLoad, GpuPower, RamLoad, RamUsed, CpuFan, CpuFanDuty];

    public static Metric? Find(string id) => All.FirstOrDefault(metric => metric.Id == id);
}
