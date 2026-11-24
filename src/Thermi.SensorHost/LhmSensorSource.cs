using LibreHardwareMonitor.Hardware;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// The only class that talks to LibreHardwareMonitor. Drives are updated every <see cref="StorageEvery"/> and send their
/// last values between: each of their updates reads SMART and opens every volume, about 3 ms of CPU and a round of disk
/// requests, for values that change slowly.
/// </summary>
internal sealed class LhmSensorSource(TimeProvider time) : ISensorSource
{
    public static readonly TimeSpan StorageEvery = TimeSpan.FromSeconds(5);

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
        IsNetworkEnabled = true,
        IsStorageEnabled = true,
        IsBatteryEnabled = true,
    };

    private readonly NetworkAdapters _adapters = new();

    // Stopping the service may close the library while a read is still running on another thread.
    private readonly Lock _lock = new();
    private readonly ReadPace _reads = new(time, TimeSpan.Zero);
    private readonly ReadPace _drives = new(time, StorageEvery);
    private readonly ReadPace _rescans = new(time, RescanEvery);
    private bool _open;

    /// <summary>
    /// How often the kinds of hardware that come and go while Windows runs are looked for again: an external GPU, a USB
    /// drive or a USB fan or pump controller would otherwise appear only once the service restarts.
    /// </summary>
    public static TimeSpan RescanEvery { get; } = TimeSpan.FromMinutes(10);

    public Snapshot Read()
    {
        lock (_lock)
        {
            if (!_open)
            {
                _computer.Open();
                _open = true;
                _rescans.Updated();
            }
            var rescanned = _rescans.Due;
            if (rescanned)
            {
                Rescan();
            }

            var adapters = _adapters.Of([.. _computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Network)
                .Select(hardware => hardware.Identifier.ToString())]);
            var sensors = new List<RawSensor>();

            // Drives found again have no values until they are updated.
            var drivesDue = _drives.Due || rescanned;
            _reads.Updated();
            if (drivesDue)
            {
                _drives.Updated();
            }

            // A filter's layer repeats its adapter's traffic, and a PC has a few for each adapter: it is neither read nor sent.
            foreach (var hardware in _computer.Hardware.Where(hardware => adapters.GetValueOrDefault(hardware.Identifier.ToString()) != AdapterKind.Layer))
            {
                var pace = hardware.HardwareType == HardwareType.Storage ? _drives : _reads;
                Collect(hardware, sensors, update: pace == _reads || drivesDue, pace.SpeedsHold);
            }

            List<string> virtualAdapters = [.. adapters.Where(adapter => adapter.Value == AdapterKind.Virtual).Select(adapter => adapter.Key)];
            return new Snapshot(sensors) { VirtualAdapters = virtualAdapters.Count > 0 ? virtualAdapters : null };
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

    // The library looks for a kind of hardware again when it is turned off and on, and only for it: the CPU, the board's
    // chip and the memory, which never come or go, keep their state, and the pipe sends the new catalog if it changed.
    private void Rescan()
    {
        _rescans.Updated();
        _computer.IsGpuEnabled = false;
        _computer.IsControllerEnabled = false;
        _computer.IsStorageEnabled = false;
        _computer.IsGpuEnabled = true;
        _computer.IsControllerEnabled = true;
        _computer.IsStorageEnabled = true;
    }

    // A hardware not updated now sends its last values; with speeds that do not hold, it sends no speed.
    private static void Collect(IHardware hardware, List<RawSensor> sensors, bool update, bool speedsHold)
    {
        try
        {
            if (update)
            {
                hardware.Update();
            }
        }
#pragma warning disable CA1031 // Drivers fail in many ways (a GPU driver reset, a busy bus); one device must not silence the rest.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Its sensors would only repeat stale values: leave them out, so they show as missing.
            return;
        }

        foreach (var sensor in hardware.Sensors)
        {
            var speed = sensor.SensorType == SensorType.Throughput;
            var raw = new RawSensor(
                sensor.Identifier.ToString(),
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType.ToString(),
                sensor.Name,
                speed && !speedsHold ? null : sensor.Value);

            // The types thermi cannot show (voltages, currents, energy, ...) never leave this process.
            if (SensorMap.KindOf(raw) is not null)
            {
                sensors.Add(raw);
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, sensors, update, speedsHold);
        }
    }
}
