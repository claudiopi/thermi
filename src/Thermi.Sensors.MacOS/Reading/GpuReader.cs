using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Core.Sensors.Rules;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The chip's GPU: its cores' temperatures from the SMC's keys of the chip's family, and their hottest; its loads and the
/// memory it uses from its accelerator's statistics, as Activity Monitor reads them; and its power from IOReport's GPU
/// energy. Only Apple's own accelerator, AGX, is the chip's GPU: a virtual machine's paravirtual one is not, and reads
/// nothing of the chip's.
/// </summary>
internal static class GpuReader
{
    public const string Accelerator = "IOAccelerator";

    public static readonly string[] Statistics = ["IOClass", "PerformanceStatistics"];

    private const string Device = "/gpu/0";
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    public static IEnumerable<RawSensor> Read(IMac mac, string chip, SmcValues smc, IReadOnlyList<ReportChannel> report, Counters counters, double? at)
    {
        var name = $"{chip} GPU";
        RawSensor Sensor(string type, int index, string sensor, double? value)
        {
            var id = string.Create(CultureInfo.InvariantCulture, $"{Device}/{type.ToLowerInvariant()}/{index}");
            return new(id, AppleSilicon.GpuHardware, name, type, sensor, value);
        }

        var sensors = new List<RawSensor>();
        var cores = smc.Keys.Where(found => found.Named.Role == AppleRole.GpuCore)
            .Select(found => (found.Named.Name, Value: BoardReader.Celsius(smc[found.Key]))).ToList();
        if (cores.Count > 0)
        {
            sensors.Add(Sensor("Temperature", 0, AppleSilicon.HottestGpuCore, cores.Max(core => core.Value)));
            sensors.AddRange(cores.Select((core, index) => Sensor("Temperature", index + 1, core.Name, core.Value)));
        }

        var statistics = mac.Registry(Accelerator, Statistics)
            .FirstOrDefault(entry => MacJson.Text(entry["IOClass"])?.StartsWith("AGX", StringComparison.Ordinal) == true)
            ?["PerformanceStatistics"] as JsonObject;
        if (statistics is not null)
        {
            sensors.Add(Sensor("Load", 0, AppleSilicon.GpuLoad, Percent(MacJson.Number(statistics["Device Utilization %"]))));
            sensors.Add(Sensor("Load", 1, "Renderer Utilization", Percent(MacJson.Number(statistics["Renderer Utilization %"]))));
            sensors.Add(Sensor("Load", 2, "Tiler Utilization", Percent(MacJson.Number(statistics["Tiler Utilization %"]))));
            sensors.Add(Sensor("Data", 0, "GPU Memory Used", MacJson.Number(statistics["In use system memory"]) / BytesPerGigabyte));
        }

        if (report.Any(channel => channel.Name.EndsWith("GPU Energy", StringComparison.Ordinal)))
        {
            sensors.Add(Sensor("Power", 0, AppleSilicon.GpuPower, Chip.Watts(report, "GPU Energy", counters, at)));
        }

        return sensors;
    }

    private static double? Percent(double? value) => value is >= 0 ? Math.Min(value.Value, 100) : null;
}
