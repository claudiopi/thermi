using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// The RAM from <c>/proc/meminfo</c>, as LibreHardwareMonitor gives Windows': its load, what is used and what is free, in
/// gigabytes. What the kernel can hand back at once, its caches among it, counts as free, as the kernel's own estimate says.
/// </summary>
internal static class MemoryReader
{
    private const string HardwareType = "Memory";
    private const string Hardware = "Total Memory";
    private const double KilobytesPerGigabyte = 1024d * 1024;

    public static IEnumerable<RawSensor> Read(IKernel kernel)
    {
        var fields = (kernel.Read("/proc/meminfo") ?? "").Split('\n')
            .Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => Counters.Number(pair[1].Replace("kB", "", StringComparison.Ordinal)), StringComparer.Ordinal);
        var total = fields.GetValueOrDefault("MemTotal");
        var free = fields.GetValueOrDefault("MemAvailable");
        var used = total - free;
        return
        [
            new("/ram/load/0", HardwareType, Hardware, "Load", "Memory", total is > 0 ? 100 * used / total : null),
            new("/ram/data/0", HardwareType, Hardware, "Data", "Memory Used", used / KilobytesPerGigabyte),
            new("/ram/data/1", HardwareType, Hardware, "Data", "Memory Available", free / KilobytesPerGigabyte),
        ];
    }
}
