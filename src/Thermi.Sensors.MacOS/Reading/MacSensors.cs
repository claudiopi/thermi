using System.Net;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// Every sensor a Mac gives, read once a second into Core's snapshot: the CPU, the GPU, the RAM, the SMC's fans, power and
/// temperatures, the HID sensors, the drives, the network adapters and the battery, with the Mac's own uptime, address and
/// clock, which Core's <see cref="SystemSensors"/> adds. The chip and its SMC keys are found at the first read, and again
/// after a key fails to read or a reader fails on what the Mac gave, which then reads nothing that second: a throw would
/// end the service, and launchd would start it again into the same failure. Adapters, drives and the battery, which come
/// and go, are read each second, and the counters that make rates are kept across finds. <paramref name="scrubbed"/>
/// moves the address into the range kept for documentation, as a dump keeps it.
/// </summary>
internal sealed class MacSensors(IMac mac, bool scrubbed = false)
{
    private const string BrandString = "machdep.cpu.brand_string";

    private readonly Counters _counters = new();
    private readonly NetworkReader _network = new();
    private Found? _found;

    public Snapshot Read()
    {
        mac.Next();
        if (_found is null || _found.Smc.Failed)
        {
            _found = Guarded(Find);
        }
        else
        {
            _found.Smc.Next();
        }

        var found = _found;
        var moment = scrubbed ? mac.Now()?.Scrubbed() : mac.Now();
        var at = moment?.Elapsed;
        var report = Guarded(() => mac.Report()) ?? [];
        IEnumerable<RawSensor>[] read =
        [
            Guarded(() => found?.Cpu.Read(mac, found.Smc, report, at).ToList()) ?? [],
            Guarded(() => found is null ? null : GpuReader.Read(mac, found.Chip, found.Smc, report, _counters, at).ToList()) ?? [],
            Guarded(() => MemoryReader.Read(mac).ToList()) ?? [],
            Guarded(() => found is null ? null : BoardReader.Read(found.Smc, mac.Hid()).ToList()) ?? [],
            Guarded(() => DriveReader.Read(mac, _counters, at).ToList()) ?? [],
            Guarded(() => _network.Read(mac, at).ToList()) ?? [],
            Guarded(() => BatteryReader.Read(mac).ToList()) ?? [],
        ];
        var snapshot = new Snapshot([.. read.SelectMany(part => part)]) { VirtualAdapters = _network.Virtual };
        var uptime = moment?.Uptime is >= 0 and < 1e12 ? TimeSpan.FromSeconds(moment.Uptime.Value) : (TimeSpan?)null;
        return SystemSensors.With(snapshot, new SystemFacts(uptime, Address(moment?.Address), moment?.LocalNow));
    }

    // What a reader that cannot make sense of what the Mac gave throws: a number past its type, a value of another type
    // than it should be, a list shorter than it says. It reads nothing, and the hardware is found again at the next read.
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

    private Found Find()
    {
        var chip = mac.Sysctl(BrandString)?.Trim() is { Length: > 0 } named ? named : "Apple silicon";
        var smc = new SmcValues(mac, chip);
        smc.Find();
        IReadOnlyList<JsonObject> manager = mac.Registry(Chip.PowerManager, [Chip.EfficiencyTable, Chip.PerformanceTable]);
        return new Found(chip, smc, new CpuReader(chip, manager, _counters));
    }

    private static uint? Address(string? address) =>
        IPAddress.TryParse(address, out var parsed) && parsed.GetAddressBytes() is { Length: 4 } bytes ? SystemSensors.AddressOf(bytes) : null;

    private sealed record Found(string Chip, SmcValues Smc, CpuReader Cpu);
}
