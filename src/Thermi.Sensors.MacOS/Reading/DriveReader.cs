using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The drives, as their block storage drivers count what they moved: the rates read and written, and the share of the
/// second they were busy, as the time spent reading and writing, which Windows' Total Activity is. A disk image is a file
/// on a drive, and no drive of its own.
/// </summary>
internal static class DriveReader
{
    private const string HardwareType = "Storage";
    private const string Driver = "IOBlockStorageDriver";
    private const string Statistics = "Statistics";
    private const string Device = MacJson.Child + "BSD Name";
    private const string Product = MacJson.Parent + "Device Characteristics/Product Name";
    private const string Location = MacJson.Parent + "Protocol Characteristics/Physical Interconnect Location";
    private const double NanosecondsPerSecond = 1e9;

    private static readonly string[] Keys = [Statistics, Device, Product, Location];

    public static IEnumerable<RawSensor> Read(IMac mac, Counters counters, double? at)
    {
        var sensors = new List<RawSensor>();
        foreach (var drive in mac.Registry(Driver, Keys))
        {
            if (MacJson.Text(drive[Device]) is not { } device || MacJson.Text(drive[Location]) == "File" || drive[Statistics] is not JsonObject statistics)
            {
                continue;
            }

            var model = MacJson.Text(drive[Product])?.Trim() is { Length: > 0 } named ? named : device;
            RawSensor Sensor(string type, int index, string name, double? value)
            {
                return new($"/storage/{device}/{type.ToLowerInvariant()}/{index}", HardwareType, model, type, name, value);
            }

            double? Count(string key)
            {
                return MacJson.Number(statistics[key]);
            }

            var busy = counters.Rate($"{device}/busy", Count("Total Time (Read)") + Count("Total Time (Write)"), at);
            sensors.Add(Sensor("Load", 0, "Total Activity", busy is { } spent ? Math.Min(100, 100 * spent / NanosecondsPerSecond) : null));
            sensors.Add(Sensor("Throughput", 0, "Read Rate", counters.Rate($"{device}/read", Count("Bytes (Read)"), at)));
            sensors.Add(Sensor("Throughput", 1, "Write Rate", counters.Rate($"{device}/write", Count("Bytes (Write)"), at)));
        }

        return sensors;
    }
}
