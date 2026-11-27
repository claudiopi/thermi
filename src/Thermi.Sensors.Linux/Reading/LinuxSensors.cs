using System.Globalization;
using System.Net;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// Every sensor the kernel gives, read once a second into Core's snapshot: the CPU, the GPUs, the RAM, the board's chips,
/// the drives, the network adapters and the batteries, with the PC's own uptime, address and clock, which Core's
/// <see cref="SystemSensors"/> adds. What changes only with the hardware, as the chips and the drives, is found at the first
/// read; adapters, which come and go, each second. <paramref name="scrubbed"/> moves the address into the range kept for
/// documentation, as a dump keeps it.
/// </summary>
internal sealed class LinuxSensors(IKernel kernel, bool scrubbed = false)
{
    private Found? _found;

    public Snapshot Read()
    {
        kernel.Next();
        var found = _found ??= Find();
        var moment = scrubbed ? kernel.Now()?.Scrubbed() : kernel.Now();
        var sensors = new List<RawSensor>();
        sensors.AddRange(found.Cpu.Read(kernel, moment?.Elapsed));
        sensors.AddRange(found.Gpus.Read());
        sensors.AddRange(MemoryReader.Read(kernel));
        foreach (var chip in found.Board)
        {
            sensors.AddRange(Hwmon.Read(kernel, chip, $"/{chip.Name}/{chip.Device}", ChipKinds.HardwareTypeOf(chip.Kind), ChipKinds.NameOf(chip.Name)));
        }

        sensors.AddRange(found.Drives.Read(kernel, moment?.Elapsed));
        sensors.AddRange(found.Network.Read(kernel, moment?.Elapsed));
        sensors.AddRange(found.Batteries.Read());
        var uptime = kernel.Read("/proc/uptime")?.Split(' ')[0] is { } seconds
            && double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var up)
            ? TimeSpan.FromSeconds(up)
            : (TimeSpan?)null;
        var snapshot = new Snapshot(sensors) { VirtualAdapters = found.Network.Virtual };
        return SystemSensors.With(snapshot, new SystemFacts(uptime, Address(moment?.Address), moment?.LocalNow));
    }

    private static uint? Address(string? address) =>
        IPAddress.TryParse(address, out var parsed) && parsed.GetAddressBytes() is { Length: 4 } bytes ? SystemSensors.AddressOf(bytes) : null;

    private Found Find()
    {
        var chips = Hwmon.Find(kernel);
        List<Chip> Of(params ChipKind[] kinds)
        {
            return [.. chips.Where(chip => kinds.Contains(chip.Kind))];
        }

        return new Found(
            new CpuReader(kernel, Of(ChipKind.Cpu)),
            new GpuReader(kernel, Of(ChipKind.GpuAmd, ChipKind.GpuIntel)),
            Of(ChipKind.SuperIO, ChipKind.EmbeddedController, ChipKind.Motherboard),
            new DriveReader(kernel, Of(ChipKind.Storage)),
            new NetworkReader(),
            new BatteryReader(kernel));
    }

    private sealed record Found(
        CpuReader Cpu, GpuReader Gpus, IReadOnlyList<Chip> Board, DriveReader Drives, NetworkReader Network, BatteryReader Batteries);
}
