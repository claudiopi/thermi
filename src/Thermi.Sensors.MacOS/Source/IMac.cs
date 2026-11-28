using System.Net;
using System.Text.Json.Nodes;

namespace Thermi.Sensors.MacOS.Source;

/// <summary>
/// What a Mac tells the readers in one second: the SMC's keys, the HID event system's temperatures, IOReport's counters,
/// the properties of IOKit's registry, sysctl's values, Mach's counters of the CPUs and the memory, the adapters'
/// counters, and the clocks. The live Mac answers through IOKit and libSystem (<see cref="LiveMac"/>), a recording from the
/// second it replays (<see cref="RecordedMac"/>), so the readers replay too. Every answer is missing, never a zero, where
/// the Mac gives none or a read fails.
/// </summary>
internal interface IMac
{
    /// <summary>The keys the SMC holds, in its order; none on a Mac with no SMC, as a virtual machine.</summary>
    IReadOnlyList<string> SmcKeys();

    /// <summary>A key's value as a number, or null when the key is missing or its read failed.</summary>
    double? Smc(string key);

    /// <summary>The HID event system's temperature sensors, each by its product's name.</summary>
    IReadOnlyList<HidReading> Hid();

    /// <summary>IOReport's channels of energy and of performance states, their counts since the Mac started; none where it has none.</summary>
    IReadOnlyList<ReportChannel> Report();

    /// <summary>
    /// The properties <paramref name="keys"/> of each service of the registry of <paramref name="className"/>, as JSON, each
    /// entry with its name under <see cref="MacJson.EntryName"/>; a key <see cref="MacJson.Parent"/> searches the service's
    /// parents, one <see cref="MacJson.Child"/> its children, and a path, as <c>Device Characteristics/Product Name</c>, one
    /// value of a dictionary.
    /// </summary>
    IReadOnlyList<JsonObject> Registry(string className, IReadOnlyList<string> keys);

    /// <summary>A sysctl's value as text, as the chip's name, or null where the Mac has none.</summary>
    string? Sysctl(string name);

    /// <summary>A sysctl's value as a number, or null where the Mac has none.</summary>
    long? SysctlNumber(string name);

    /// <summary>Each CPU's ticks of user, system, idle and nice time, or null where Mach refused.</summary>
    IReadOnlyList<long[]>? CpuTicks();

    /// <summary>The memory's counts of pages, or null where Mach refused.</summary>
    MemoryPages? Memory();

    /// <summary>Each network adapter, by its BSD name, with its kind and the bytes it moved.</summary>
    IReadOnlyList<Adapter> Adapters();

    /// <summary>The clocks, the uptime and the address now, or null when a replay recorded none.</summary>
    Moment? Now();

    /// <summary>Moves to the next second's answers, before each read of the sensors.</summary>
    void Next();
}

/// <summary>A HID temperature sensor: its product's name, as <c>PMU tdie1</c>, and degrees, or null where it read none.</summary>
internal sealed record HidReading(string Product, double? Value);

/// <summary>
/// One of IOReport's channels: its group, subgroup and name, and its unit, as <c>mJ</c>; either one count, as energy
/// used, or, for a channel of states, the time spent in each state, in order.
/// </summary>
internal sealed record ReportChannel(string Group, string? Subgroup, string Name, string? Unit)
{
    public long? Value { get; init; }

    public IReadOnlyList<ReportState>? States { get; init; }
}

/// <summary>A performance state, as <c>V0P7</c>, or <c>IDLE</c>, and the time spent in it.</summary>
internal sealed record ReportState(string Name, long Residency);

/// <summary>
/// The memory's pages as Mach counts them, as <c>vm_stat</c> shows them: free, wired, held by the compressor, of
/// processes' own memory, and of it what may be purged.
/// </summary>
internal sealed record MemoryPages(long Free, long Wired, long Compressor, long Internal, long Purgeable);

/// <summary>A network adapter: its BSD name, its interface type, as 6 for Ethernet, whether it loops back, and its counters of bytes.</summary>
internal sealed record Adapter(string Name, int Type, bool Loopback, long Received, long Sent);

/// <summary>
/// The clocks and the address at a second: <see cref="Elapsed"/>, in seconds on a clock that only goes forward, times every
/// rate; <see cref="LocalNow"/> is the wall clock, in the Mac's time zone; <see cref="Uptime"/> the seconds since it started;
/// <see cref="Address"/> the local IPv4 address the default route goes out from, or null with no route out.
/// </summary>
internal sealed record Moment(double Elapsed, DateTimeOffset LocalNow, double? Uptime, string? Address)
{
    /// <summary>The moment as a recording or a dump keeps it: its address in the range kept for documentation, its last number kept.</summary>
    public Moment Scrubbed() => this with
    {
        Address = IPAddress.TryParse(Address, out var parsed) && parsed.GetAddressBytes() is [_, _, _, var last]
            ? new IPAddress([192, 0, 2, last]).ToString()
            : null,
    };
}
