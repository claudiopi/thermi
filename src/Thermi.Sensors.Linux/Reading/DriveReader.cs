using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// Each physical drive in <c>/sys/block</c>, its activity and speeds from <c>/proc/diskstats</c> and its
/// temperatures from its <c>hwmon</c> chip, as NVMe's, all under one device, the drive's own under <c>/sys/devices</c>. A
/// drive under <c>/sys/devices/virtual</c>, as a loop device or a RAM disk, is none, nor is one the kernel calls removable,
/// as an optical drive or a card reader, so the disk metrics follow only the PC's own drives.
/// </summary>
internal sealed class DriveReader
{
    private const string HardwareType = "Storage";
    private const string Block = "/sys/block";

    // /proc/diskstats counts in sectors of 512 bytes, whatever the drive's own.
    private const double SectorBytes = 512;

    private readonly IReadOnlyList<Drive> _drives;
    private readonly IReadOnlyList<(Chip Chip, string Model)> _chips;
    private readonly Counters _counters;

    /// <summary>The drives as the kernel shows them this second, with the counters their speeds keep across seconds.</summary>
    public DriveReader(IKernel kernel, IReadOnlyList<Chip> chips, Counters counters)
    {
        _counters = counters;
        _drives = [.. kernel.List(Block).Select(name => DriveOf(kernel, name)).OfType<Drive>()];
        _chips = [.. chips.Select(chip => (chip, _drives.FirstOrDefault(drive => drive.Device == chip.Device)?.Model ?? chip.Name))];
    }

    public IEnumerable<RawSensor> Read(IKernel kernel, double? at)
    {
        var stats = (kernel.Read("/proc/diskstats") ?? "").Split('\n')
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length >= 14)
            .ToDictionary(fields => fields[2], fields => fields, StringComparer.Ordinal);
        foreach (var (chip, model) in _chips)
        {
            foreach (var sensor in Hwmon.Read(kernel, chip, IdOf(chip.Device), HardwareType, model))
            {
                yield return sensor;
            }
        }

        foreach (var drive in _drives)
        {
            var fields = stats.GetValueOrDefault(drive.Name);
            double? Rate(string counter, int field)
            {
                return _counters.Rate($"{drive.Name}/{counter}", fields is null ? null : Counters.Number(fields[field]), at);
            }

            RawSensor Sensor(string type, int index, string name, double? value)
            {
                return new($"{IdOf(drive.Device)}/{type.ToLowerInvariant()}/{index}", HardwareType, drive.Model, type, name, value);
            }

            // The milliseconds of each second it was busy, as a percent.
            yield return Sensor("Load", 0, "Total Activity", Rate("busy", 12) / 10);
            yield return Sensor("Throughput", 0, "Read Rate", Rate("read", 5) * SectorBytes);
            yield return Sensor("Throughput", 1, "Write Rate", Rate("write", 9) * SectorBytes);
        }
    }

    private static string IdOf(string device) => $"/storage/{device}";

    private static Drive? DriveOf(IKernel kernel, string name)
    {
        if (kernel.Resolve($"{Block}/{name}") is not { } real
            || real.StartsWith("/sys/devices/virtual/", StringComparison.Ordinal)
            || kernel.Read($"{Block}/{name}/removable")?.Trim() == "1")
        {
            return null;
        }

        var device = kernel.Resolve($"{Block}/{name}/device") ?? real;
        var model = kernel.Read($"{Block}/{name}/device/model")?.Trim() is { Length: > 0 } named ? named : name;
        return new Drive(name, Hwmon.DevicePath(device), model);
    }

    private sealed record Drive(string Name, string Device, string Model);
}
