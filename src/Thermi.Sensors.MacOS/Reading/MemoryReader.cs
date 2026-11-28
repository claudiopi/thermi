using Thermi.Core.Sensors;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The RAM, as Activity Monitor counts it and LibreHardwareMonitor names Windows': what is used, the memory of the apps
/// that cannot be purged, the wired memory and what the compressor holds; what is available, the rest; and their share, in
/// gigabytes, from Mach's counts of pages.
/// </summary>
internal static class MemoryReader
{
    private const string HardwareType = "Memory";
    private const string Hardware = "Total Memory";
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    public static IEnumerable<RawSensor> Read(IMac mac)
    {
        var total = mac.SysctlNumber("hw.memsize");
        var page = mac.SysctlNumber("hw.pagesize");
        var pages = mac.Memory();
        double? used = pages is null ? null : (pages.Internal - pages.Purgeable + pages.Wired + pages.Compressor) * page;
        double? free = total - used;
        return
        [
            new("/ram/load/0", HardwareType, Hardware, "Load", "Memory",
                total is > 0 && used is { } bytes ? Math.Clamp(100 * bytes / total.Value, 0, 100) : null),
            new("/ram/data/0", HardwareType, Hardware, "Data", "Memory Used", used / BytesPerGigabyte),
            new("/ram/data/1", HardwareType, Hardware, "Data", "Memory Available", free / BytesPerGigabyte),
        ];
    }
}
