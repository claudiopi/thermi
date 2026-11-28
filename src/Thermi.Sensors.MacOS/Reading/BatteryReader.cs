using System.Text.Json;
using Thermi.Core.Sensors;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// A MacBook's battery, as its smart battery tells IOKit's registry and LibreHardwareMonitor names Windows': the charge,
/// the rate it charges at, below zero while it discharges, and the time left. A Mac with no battery, as a Mac mini, whose
/// registry still lists one not installed, has none.
/// </summary>
internal static class BatteryReader
{
    public const string Battery = "AppleSmartBattery";

    public static readonly string[] Keys = ["BatteryInstalled", "CurrentCapacity", "MaxCapacity", "Amperage", "Voltage", "AvgTimeToEmpty"];

    private const string HardwareType = "Battery";
    private const string Hardware = "Battery";
    private const string Id = "/battery/0";

    // What the battery says of a time it cannot tell yet, in minutes.
    private const double Unknown = 65535;

    public static IEnumerable<RawSensor> Read(IMac mac)
    {
        var battery = mac.Registry(Battery, Keys).FirstOrDefault(entry => entry["BatteryInstalled"]?.GetValueKind() == JsonValueKind.True);
        if (battery is null)
        {
            return [];
        }

        double? Value(string key)
        {
            return MacJson.Number(battery[key]);
        }

        // Milliamperes, below zero while it discharges, at millivolts.
        var watts = Value("Amperage") * Value("Voltage") / 1e6;
        var capacity = Value("MaxCapacity");
        var minutes = Value("AvgTimeToEmpty");
        return
        [
            new($"{Id}/level/0", HardwareType, Hardware, "Level", "Charge Level",
                capacity is > 0 && Value("CurrentCapacity") is { } left ? Math.Clamp(100 * left / capacity.Value, 0, 100) : null),
            new($"{Id}/power/0", HardwareType, Hardware, "Power", "Charge/Discharge Rate", watts),
            new($"{Id}/timespan/0", HardwareType, Hardware, "TimeSpan", "Remaining Time (Estimated)",
                watts is < 0 && minutes is >= 0 and < Unknown ? minutes * 60 : null),
            new($"{Id}/voltage/0", HardwareType, Hardware, "Voltage", "Voltage", Value("Voltage") / 1e3),
        ];
    }
}
