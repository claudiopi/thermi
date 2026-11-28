using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Sensors.MacOS.Native;

namespace Thermi.Sensors.MacOS.Source;

/// <summary>
/// The Mac itself, read as the person: the SMC's user client, the HID event system and an IOReport subscription, opened
/// once and held for as long as the service runs, and IOKit's registry, sysctl and Mach's counters, asked each time.
/// </summary>
internal sealed class LiveMac(TimeProvider time) : IMac, IDisposable
{
    // struct timeval, as kern.boottime answers it: seconds, then microseconds.
    private const int BootTimeSize = 16;

    // Where vm_statistics64 holds the counts it is read for, each a 32-bit number of pages.
    private const int FreeAt = 0;
    private const int WiredAt = 12;
    private const int PurgeableAt = 88;
    private const int CompressorAt = 128;
    private const int InternalAt = 140;

    private readonly Smc _smc = new();
    private readonly HidSensors _hid = new();
    private readonly Reports _reports = new();

    public IReadOnlyList<string> SmcKeys() => _smc.Keys();

    public double? Smc(string key) => _smc.Read(key);

    public IReadOnlyList<HidReading> Hid() => _hid.Read();

    public IReadOnlyList<ReportChannel> Report() => _reports.Read();

    public IReadOnlyList<JsonObject> Registry(string className, IReadOnlyList<string> keys) => IOKitLibrary.Registry(className, keys);

    public string? Sysctl(string name) => SystemLibrary.Sysctl(name) is { } bytes ? Encoding.UTF8.GetString(bytes).TrimEnd('\0') : null;

    public long? SysctlNumber(string name) => SystemLibrary.Sysctl(name) switch
    {
        { Length: sizeof(int) } bytes => BinaryPrimitives.ReadInt32LittleEndian(bytes),
        { Length: sizeof(long) } bytes => BinaryPrimitives.ReadInt64LittleEndian(bytes),
        _ => null,
    };

    public IReadOnlyList<long[]>? CpuTicks() =>
        SystemLibrary.CpuTicks() is { } ticks ? [.. ticks.Select(cpu => cpu.Select(tick => (long)tick).ToArray())] : null;

    public MemoryPages? Memory()
    {
        if (SystemLibrary.MemoryPages() is not { } pages)
        {
            return null;
        }

        long Count(int at)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(pages.AsSpan(at));
        }

        return new MemoryPages(Count(FreeAt), Count(WiredAt), Count(CompressorAt), Count(InternalAt), Count(PurgeableAt));
    }

    public IReadOnlyList<Adapter> Adapters() => Interfaces.Read();

    public Moment Now()
    {
        var now = time.GetLocalNow();
        var booted = SystemLibrary.Sysctl("kern.boottime") is { Length: BootTimeSize } boot
            ? BinaryPrimitives.ReadInt64LittleEndian(boot) + (BinaryPrimitives.ReadInt32LittleEndian(boot.AsSpan(sizeof(long))) / 1e6)
            : (double?)null;
        var address = SystemSensors.DefaultRouteAddress() is { } route
            ? new IPAddress([(byte)(route >> 24), (byte)(route >> 16), (byte)(route >> 8), (byte)route]).ToString()
            : null;
        return new Moment((double)time.GetTimestamp() / time.TimestampFrequency, now, (now.ToUnixTimeMilliseconds() / 1e3) - booted, address);
    }

    Moment? IMac.Now() => Now();

    public void Next()
    {
        // The Mac answers what it holds now; there is nothing to move to.
    }

    public void Dispose()
    {
        _smc.Dispose();
        _hid.Dispose();
        _reports.Dispose();
    }
}
