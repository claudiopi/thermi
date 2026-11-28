using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// A laptop's batteries, in <c>/sys/class/power_supply</c>, and never a device's, as a Bluetooth mouse's, which the kernel
/// lists beside them, of the scope <c>Device</c>, and names by its address: the charge, the rate it charges at, below zero
/// while it discharges, and the time left, as LibreHardwareMonitor names Windows'. A battery reports its power, or its
/// current and voltage, and its energy or its charge: either pair gives the rate and the time.
/// </summary>
internal sealed class BatteryReader(IKernel kernel)
{
    private const string HardwareType = "Battery";
    private const string Supplies = "/sys/class/power_supply";

    private readonly IReadOnlyList<string> _batteries =
        [.. kernel.List(Supplies).Where(supply => kernel.Read($"{Supplies}/{supply}/type")?.Trim() == "Battery"
            && kernel.Read($"{Supplies}/{supply}/scope")?.Trim() != "Device")];

    public IEnumerable<RawSensor> Read()
    {
        foreach (var battery in _batteries)
        {
            double? Value(string file)
            {
                return Counters.Number(kernel.Read($"{Supplies}/{battery}/{file}"));
            }

            // Some drivers sign the rate while discharging, as the status already says.
            double? Size(string file)
            {
                return Value(file) is { } value ? Math.Abs(value) : null;
            }

            var discharging = kernel.Read($"{Supplies}/{battery}/status")?.Trim() == "Discharging";
            var volts = Value("voltage_now") / 1e6;
            // Microwatts, or microamperes at the voltage; microwatt-hours, or microampere-hours at it.
            var watts = (Size("power_now") / 1e6) ?? (Size("current_now") / 1e6 * volts);
            var hours = (Value("energy_now") / 1e6) ?? (Value("charge_now") / 1e6 * volts);
            var id = $"/battery/{battery}";
            yield return new($"{id}/level/0", HardwareType, battery, "Level", "Charge Level", Value("capacity"));
            yield return new($"{id}/power/0", HardwareType, battery, "Power", "Charge/Discharge Rate", discharging ? -watts : watts);
            yield return new($"{id}/timespan/0", HardwareType, battery, "TimeSpan", "Remaining Time (Estimated)",
                discharging && watts is > 0 ? hours / watts * 3600 : null);
            yield return new($"{id}/voltage/0", HardwareType, battery, "Voltage", "Voltage", volts);
        }
    }
}
