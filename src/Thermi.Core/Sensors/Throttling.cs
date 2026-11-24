using Thermi.Core.Settings;

namespace Thermi.Core.Sensors;

/// <summary>A CPU that throttles: its clock and its base clock, in MHz, and its temperature, in °C, as its tooltip says them.</summary>
public sealed record Throttled(double Clock, double BaseClock, double Temperature);

/// <summary>
/// Whether the CPU throttles: its clock under its base clock while it is hot, at or above the warning its defaults give
/// it, in one snapshot. The library reports no throttling of its own, of a CPU or a GPU, so this is the sign.
/// </summary>
public static class Throttling
{
    /// <summary>
    /// The throttling of the CPU an overlay reads, by the sensors it names for CPU clock and CPU temperature, or null while it
    /// does not throttle, either reads nothing, or <paramref name="baseClock"/>, in MHz, is unknown.
    /// </summary>
    public static Throttled? Of(Snapshot snapshot, OverlayConfig overlay, double? baseClock)
    {
        if (baseClock is not { } nominal)
        {
            return null;
        }

        var readings = SensorMap.Read(snapshot, overlay);
        var hot = Threshold.Default(new MeterConfig { Metric = Metrics.CpuTemperature.Id }, snapshot)?.Warning;
        return readings[Metrics.CpuClock.Id].Usable is { } clock
            && readings[Metrics.CpuTemperature.Id].Usable is { } temperature
            && clock < nominal
            && temperature >= hot
            ? new Throttled(clock, nominal, temperature)
            : null;
    }

    /// <summary>
    /// Whether an item reads the CPU's temperature or its clock, by its metric or by a CPU sensor of those types,
    /// and so shows the CPU's throttling.
    /// </summary>
    public static bool Shows(ItemConfig item, Snapshot snapshot) =>
        ((item.Metric ?? item.Auto) is { } metric && (metric == Metrics.CpuTemperature.Id || metric == Metrics.CpuClock.Id))
        || (item.Sensor is { } id
            && SensorMap.Find(snapshot, id) is { HardwareType: "Cpu", Type: nameof(SensorKind.Temperature) or nameof(SensorKind.Clock) });
}
