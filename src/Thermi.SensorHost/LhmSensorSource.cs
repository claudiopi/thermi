using LibreHardwareMonitor.Hardware;
using Thermi.Core;

namespace Thermi.SensorHost;

/// <summary>The only class that talks to LibreHardwareMonitor.</summary>
internal sealed class LhmSensorSource : IDisposable
{
    // The sensor types thermi can show. The rest (voltages, throughput, ...) never leave this process.
    private static readonly HashSet<SensorType> Shown = [SensorType.Temperature, SensorType.Load, SensorType.Clock, SensorType.Power, SensorType.Fan, SensorType.Control, SensorType.Data];

    // Storage and network stay off: they cost the most to read, and thermi has no metric for them.
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
    };

    // Stopping the service may close the library while a read is still running on another thread.
    private readonly Lock _lock = new();
    private bool _open;

    public Snapshot Read()
    {
        lock (_lock)
        {
            if (!_open)
            {
                _computer.Open();
                _open = true;
            }

            var sensors = new List<RawSensor>();
            foreach (var hardware in _computer.Hardware)
            {
                Collect(hardware, sensors);
            }

            return new Snapshot(sensors);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_open)
            {
                _computer.Close();
                _open = false;
            }
        }
    }

    private static void Collect(IHardware hardware, List<RawSensor> sensors)
    {
        try
        {
            hardware.Update();
        }
#pragma warning disable CA1031 // Drivers fail in many ways (a GPU driver reset, a busy bus); one device must not silence the rest.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Its sensors would only repeat stale values: leave them out, so they show as missing.
            return;
        }

        foreach (var sensor in hardware.Sensors.Where(sensor => Shown.Contains(sensor.SensorType)))
        {
            sensors.Add(new RawSensor(
                sensor.Identifier.ToString(),
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType.ToString(),
                sensor.Name,
                sensor.Value));
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, sensors);
        }
    }
}
