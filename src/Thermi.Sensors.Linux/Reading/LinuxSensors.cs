using System.Globalization;
using System.Net;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// Every sensor the kernel gives, read once a second into Core's snapshot: the CPU, the GPUs, the RAM, the board's chips,
/// the drives, the network adapters and the batteries, with the PC's own uptime, address and clock, which Core's
/// <see cref="SystemSensors"/> adds. The hardware is found at the first read, again whenever <c>hwmon</c>'s chips change,
/// as a USB pump plugged again may take another chip's <c>hwmonN</c>, and every <see cref="RescanEvery"/> reads, as
/// Windows' service rescans, for a module that loads or a drive that comes; a chip is known by its device's path, which a
/// rescan keeps where the kernel renumbers <c>hwmonN</c>. A board's chip that costs much to read is read less often
/// (<see cref="ChipPace"/>). Adapters, which come and go, are listed each second, and the counters that make rates are
/// kept across rescans. A read never throws: a reader that fails on what the
/// kernel gave reads nothing that second, and the hardware is found again at the next, as a throw would end the service,
/// and systemd would start it again into the same failure until it gave up. <paramref name="scrubbed"/> moves the
/// address into the range kept for documentation, as a dump keeps it.
/// </summary>
internal sealed class LinuxSensors(IKernel kernel, bool scrubbed = false)
{
    /// <summary>Reads between rescans: ten minutes of a read a second.</summary>
    public const int RescanEvery = 600;

    private readonly Counters _cpu = new();
    private readonly Counters _drives = new();
    private readonly NetworkReader _network = new();
    private Found? _found;
    private string? _chips;
    private int _reads;

    public Snapshot Read()
    {
        kernel.Next();
        var chips = Hwmon.Listing(kernel);
        if (_found is null || chips != _chips || ++_reads % RescanEvery == 0)
        {
            _chips = chips;
            _found = Guarded(Find);
        }

        var found = _found;
        var moment = scrubbed ? kernel.Now()?.Scrubbed() : kernel.Now();
        var sensors = new List<RawSensor>();
        IEnumerable<RawSensor>[] read =
        [
            Guarded(() => found?.Cpu.Read(kernel, moment?.Elapsed).ToList()) ?? [],
            Guarded(() => found?.Gpus.Read().ToList()) ?? [],
            Guarded(() => MemoryReader.Read(kernel).ToList()) ?? [],
            Guarded(() => found is null ? null : Board(found, moment?.Elapsed)) ?? [],
            Guarded(() => found?.Drives.Read(kernel, moment?.Elapsed).ToList()) ?? [],
            Guarded(() => _network.Read(kernel, moment?.Elapsed).ToList()) ?? [],
            Guarded(() => found?.Batteries.Read().ToList()) ?? [],
        ];
        sensors.AddRange(read.SelectMany(part => part));
        // A number past what a TimeSpan holds is no uptime.
        var uptime = kernel.Read("/proc/uptime")?.Split(' ')[0] is { } seconds
            && double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var up)
            && up is >= 0 and < 1e12
            ? TimeSpan.FromSeconds(up)
            : (TimeSpan?)null;
        var snapshot = new Snapshot(sensors) { VirtualAdapters = _network.Virtual };
        return SystemSensors.With(snapshot, new SystemFacts(uptime, Address(moment?.Address), moment?.LocalNow));
    }

    // What a reader that cannot make sense of a file throws: a number past its type, a list shorter than it says, a key
    // twice. It reads nothing, and the hardware is found again at the next read.
    private T? Guarded<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException
            or InvalidOperationException or KeyNotFoundException)
        {
            _found = null;
            return null;
        }
    }

    private List<RawSensor> Board(Found found, double? at) =>
        [.. found.Board.SelectMany(chip => found.Pace.Read(kernel, chip, at, () =>
            [.. Hwmon.Read(kernel, chip, $"/{chip.Name}/{chip.Device}", ChipKinds.HardwareTypeOf(chip.Kind), ChipKinds.NameOf(chip.Name))]))];

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
            new CpuReader(kernel, Of(ChipKind.Cpu), _cpu),
            new GpuReader(kernel, Of(ChipKind.GpuAmd, ChipKind.GpuIntel)),
            Of(ChipKind.SuperIO, ChipKind.EmbeddedController, ChipKind.Motherboard),
            new DriveReader(kernel, Of(ChipKind.Storage), _drives),
            new BatteryReader(kernel),
            new ChipPace());
    }

    private sealed record Found(
        CpuReader Cpu, GpuReader Gpus, IReadOnlyList<Chip> Board, DriveReader Drives, BatteryReader Batteries, ChipPace Pace);
}
