using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rules;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// What the SMC tells beyond the chip, as a laptop's embedded controller on Windows: each fan's speed and its share of its
/// highest, the Mac's power and the DC input's voltage, and the temperatures of its memory, SSD, Wi-Fi and airflow; and the
/// HID event system's temperatures, each by the name its sensor gives.
/// </summary>
internal static class BoardReader
{
    private const string Controller = "EmbeddedController";
    private const string ControllerName = "Apple SMC";
    private const string Motherboard = "Motherboard";
    private const string HidName = "HID Sensors";

    // Degrees below which a reading is no sensor's.
    private const double Coldest = 5;

    /// <summary>
    /// A temperature in degrees, or null for one no sensor reads: the SMC answers a key of a sensor the chip lacks with a
    /// value near 0, as a Mac mini M4's GPU keys read 0.8 and -4.5, or with -127, and no chip that runs is that cold.
    /// </summary>
    public static double? Celsius(double? value) => value is >= Coldest and < 150 ? value : null;

    public static IEnumerable<RawSensor> Read(SmcValues smc, IReadOnlyList<HidReading> hid)
    {
        var sensors = new List<RawSensor>();
        for (var fan = 0; fan < smc.Fans; fan++)
        {
            var (speed, highest) = AppleSilicon.FanKeys(fan);
            var rpm = smc[speed];
            var most = smc[highest];
            var name = string.Create(CultureInfo.InvariantCulture, $"Fan #{fan + 1}");
            sensors.Add(Sensor(Controller, ControllerName, "/smc", "Fan", fan, name, rpm is >= 0 ? rpm : null));
            var duty = rpm is >= 0 && most is > 0 ? Math.Min(100, 100 * rpm.Value / most.Value) : (double?)null;
            sensors.Add(Sensor(Controller, ControllerName, "/smc", "Control", fan, name, duty));
        }

        var named = smc.Keys.Where(found => found.Named.Role is AppleRole.Board or AppleRole.Memory or AppleRole.Power or AppleRole.Voltage).ToList();
        foreach (var ((key, apple), index) in named.Select((found, index) => (found, index)))
        {
            var value = smc[key];
            var (type, read) = apple.Role switch
            {
                AppleRole.Power => ("Power", value is >= 0 ? value : null),
                AppleRole.Voltage => ("Voltage", value is >= 0 ? value : null),
                AppleRole.Board or AppleRole.PerformanceCore or AppleRole.EfficiencyCore or AppleRole.GpuCore or AppleRole.Memory =>
                    ("Temperature", Celsius(value)),
                _ => throw new ArgumentOutOfRangeException(nameof(smc), apple.Role, "Unknown role."),
            };
            sensors.Add(Sensor(Controller, ControllerName, "/smc", type, index, apple.Name, read));
        }

        sensors.AddRange(hid.Select((sensor, index) => Sensor(Motherboard, HidName, "/hid", "Temperature", index, sensor.Product, Celsius(sensor.Value))));
        return sensors;
    }

    private static RawSensor Sensor(string hardwareType, string hardware, string device, string type, int index, string name, double? value) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{device}/{type.ToLowerInvariant()}/{index}"), hardwareType, hardware, type, name, value);
}
