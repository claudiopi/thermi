using Thermi.Core.Sensors;

namespace Thermi.Sensors.Windows.Reading;

/// <summary>What the service mends in what the library reads, before it sends it.</summary>
internal static class ReadingRules
{
    private const string Storage = "Storage";

    /// <summary>
    /// A drive's activity and rates come from two samples of its counters, and the library's first update of a drive it
    /// made, at the service's start or a rescan, has one: no rates, and an activity that reads all the time since Windows
    /// started as busy. So the activity reads nothing whenever the rates do, as after a gap, when it would be an average.
    /// </summary>
    public static IReadOnlyList<RawSensor> Sampled(IReadOnlyList<RawSensor> sensors)
    {
        var unsampled = sensors
            .Where(sensor => sensor.HardwareType == Storage && sensor.Type == nameof(SensorKind.Throughput) && sensor.Value is null)
            .Select(SensorMap.DeviceIdOf)
            .ToHashSet(StringComparer.Ordinal);
        return unsampled.Count == 0
            ? sensors
            : [.. sensors.Select(sensor => IsActivity(sensor) && unsampled.Contains(SensorMap.DeviceIdOf(sensor)) ? sensor with { Value = null } : sensor)];
    }

    /// <summary>
    /// A device's name as the window shows it. The library names a memory module by its chip's part number, which can end
    /// in a NUL with leftover bytes behind it, before the slot number the library adds as its last word, or in spaces and a
    /// stray brace.
    /// </summary>
    public static string NameOf(string name)
    {
        var end = name.AsSpan().IndexOfAnyInRange('\0', '\u001f');
        var last = name.LastIndexOf(' ');
        var kept = end < 0 ? name : name[..end] + (last > end ? name[last..] : "");
        return string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Trim('{', '}').Length > 0));
    }

    private static bool IsActivity(RawSensor sensor) =>
        sensor.HardwareType == Storage && sensor.Type == nameof(SensorKind.Load) && sensor.Name.EndsWith(" Activity", StringComparison.Ordinal);
}
