using System.Net;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// What Linux tells the readers in one second: the files under <c>/sys</c> and <c>/proc</c>, where their links lead, and
/// what is no file, the clocks, the address the default route goes out from and NVML's answers. The live system answers
/// from <c>/</c>, a recorded tree from the second it replays (<see cref="RecordedKernel"/>), so the readers replay too.
/// </summary>
internal interface IKernel
{
    /// <summary>A file's text, or null when it is missing, unreadable or fails mid-read: a missing value, never a zero.</summary>
    string? Read(string path);

    /// <summary>The names in a folder, in ordinal order, so a replay lists as the live system did; none when it is missing.</summary>
    IReadOnlyList<string> List(string folder);

    /// <summary>The path <paramref name="path"/> leads to, every link on the way followed, or null when nothing is there.</summary>
    string? Resolve(string path);

    /// <summary>The clocks and the address now, or null when a replay recorded none.</summary>
    Moment? Now();

    /// <summary>What NVML says of each NVIDIA GPU, or null when NVIDIA's driver is not installed.</summary>
    IReadOnlyList<GpuAnswer>? Nvml();

    /// <summary>Moves to the next second's answers, before each read of the sensors.</summary>
    void Next();

    /// <summary>Whether a path is the kernel's, under <c>/sys</c> or <c>/proc</c>: all a reader reads, and all a tree keeps.</summary>
    static bool Holds(string path) => path.StartsWith("/sys/", StringComparison.Ordinal) || path.StartsWith("/proc/", StringComparison.Ordinal);
}

/// <summary>
/// The clocks and the address at a second: <see cref="Elapsed"/>, in seconds on a clock that only goes forward, times every
/// rate; <see cref="LocalNow"/> is the wall clock, in the PC's time zone; <see cref="Address"/> the local IPv4 address the
/// default route goes out from, or null with no route out.
/// </summary>
internal sealed record Moment(double Elapsed, DateTimeOffset LocalNow, string? Address)
{
    /// <summary>The moment as a recording or a dump keeps it: its address in the range kept for documentation, its last number kept.</summary>
    public Moment Scrubbed() => this with
    {
        Address = IPAddress.TryParse(Address, out var parsed) && parsed.GetAddressBytes() is [_, _, _, var last]
            ? new IPAddress([192, 0, 2, last]).ToString()
            : null,
    };
}

/// <summary>
/// What NVML answered of one GPU, each value null where it answered an error: degrees, percents, watts, a fan's percent of
/// its speed, gigabytes and megahertz.
/// </summary>
internal sealed record GpuAnswer(string Name)
{
    public double? Temperature { get; init; }

    public double? Load { get; init; }

    public double? MemoryLoad { get; init; }

    public double? Power { get; init; }

    public double? Fan { get; init; }

    public double? MemoryUsed { get; init; }

    public double? MemoryTotal { get; init; }

    public double? CoreClock { get; init; }

    public double? MemoryClock { get; init; }
}
