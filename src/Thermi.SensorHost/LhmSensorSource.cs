using LibreHardwareMonitor.Hardware;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// The only class that talks to LibreHardwareMonitor. Drives are updated every <see cref="StorageEvery"/> and send their
/// last values between: each of their updates reads SMART and opens every volume, about 3 ms of CPU and a round of disk
/// requests, for values that change slowly. A device whose update stalls or fails holds back none of the others
/// (<see cref="DeviceUpdates"/>), and <c>slow</c> is told its name, how long it took and how it failed.
/// </summary>
internal sealed class LhmSensorSource(TimeProvider time, Action<string, TimeSpan, Exception?> slow) : ISensorSource
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
    private readonly SemaphoreSlim _gate = new(1);
    private readonly ReadPace _reads = new(time, TimeSpan.Zero);
    private readonly ReadPace _drives = new(time, StorageEvery);
    private readonly ReadPace _rescans = new(time, RescanEvery);
    private readonly DeviceUpdates _updates = new(time, slow, SensorPipeServer.End);
    private bool _open;

    /// <summary>
    /// How often the kinds of hardware that come and go while Windows runs are looked for again: an external GPU, a USB
    /// drive or a USB fan or pump controller would otherwise appear only once the service restarts.
    /// </summary>
    public static TimeSpan RescanEvery { get; } = TimeSpan.FromMinutes(10);

    public async Task<Snapshot> ReadAsync(CancellationToken stop)
    {
        await _gate.WaitAsync(stop);
        try
        {
            var rescanned = OpenOrRescan();
            var adapters = _adapters.Of([.. _computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Network)
                .Select(hardware => hardware.Identifier.ToString())]);

            // Drives found again have no values until they are updated.
            var drivesDue = _drives.Due || rescanned;
            _reads.Updated();
            if (drivesDue)
            {
                _drives.Updated();
            }

            // A filter's layer repeats its adapter's traffic, and a PC has a few for each adapter: it is neither read nor sent.
            var devices = _computer.Hardware
                .Where(hardware => adapters.GetValueOrDefault(hardware.Identifier.ToString()) != AdapterKind.Layer)
                .SelectMany(hardware => Each(hardware).Select(device => (Device: device, Pace: PaceOf(hardware))))
                .ToList();
            var read = await _updates.UpdateAsync(
                [.. devices.Select(each => new DeviceUpdate(
                    each.Device.Identifier.ToString(),
                    each.Device.Name,
                    () => Task.Run(each.Device.Update, CancellationToken.None),
                    Due: each.Pace == _reads || drivesDue))],
                stop);
            var sensors = new List<RawSensor>();
            foreach (var (device, pace) in devices)
            {
                Collect(device, sensors, read[device.Identifier.ToString()], pace.SpeedsHold);
            }

            List<string> virtualAdapters = [.. adapters.Where(adapter => adapter.Value == AdapterKind.Virtual).Select(adapter => adapter.Key)];
            return new Snapshot(sensors) { VirtualAdapters = virtualAdapters.Count > 0 ? virtualAdapters : null };
        }
        finally
        {
            _gate.Release();
        }
    }

    // An update still running when the service stops is left to the process's end, which closes the library's handles too.
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_open && !_updates.Busy)
            {
                _computer.Close();
                _open = false;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    // A device and the ones on it, each updated on its own, as the library's own update visits them.
    private static IEnumerable<IHardware> Each(IHardware hardware) => [hardware, .. hardware.SubHardware.SelectMany(Each)];

    private ReadPace PaceOf(IHardware hardware) => hardware.HardwareType == HardwareType.Storage ? _drives : _reads;

    private bool OpenOrRescan()
    {
        if (!_open)
        {
            _computer.Open();
            _open = true;
            _rescans.Updated();
        }

        // Not while a stalled update runs: looking again closes the devices it may be updating.
        var rescanned = _rescans.Due && !_updates.Busy;
        if (rescanned)
        {
            Rescan();
        }

        return rescanned;
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

    // A device not updated now sends its last values, and with speeds that do not hold, no speed; one that never read
    // has no values to send, so it is left out and shows as missing.
    private static void Collect(IHardware device, List<RawSensor> sensors, DeviceRead read, bool speedsHold)
    {
        if (read == DeviceRead.Failed)
        {
            return;
        }

        foreach (var sensor in device.Sensors)
        {
            var speed = sensor.SensorType == SensorType.Throughput;
            var raw = new RawSensor(
                sensor.Identifier.ToString(),
                device.HardwareType.ToString(),
                device.Name,
                sensor.SensorType.ToString(),
                sensor.Name,
                read == DeviceRead.Missing || (speed && !speedsHold) ? null : sensor.Value);

            // The types thermi cannot show (voltages, currents, energy, ...) never leave this process.
            if (SensorMap.KindOf(raw) is not null)
            {
                sensors.Add(raw);
            }
        }
    }
}
