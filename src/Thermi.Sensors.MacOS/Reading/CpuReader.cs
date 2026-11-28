using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Core.Sensors.Rules;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The CPU, as LibreHardwareMonitor names Windows': its cores' temperatures from the SMC's keys of the chip's family, and
/// the hottest of them, which is the CPU's temperature; its power from IOReport's CPU energy; each core's load from Mach's
/// ticks; and each core's clock from IOReport's performance states, with their average. Logical CPUs and IOReport's
/// cores count the efficiency cores first, then the performance ones, so a core's number is the same in each.
/// </summary>
internal sealed class CpuReader(string chip, IReadOnlyList<JsonObject> manager, Counters counters)
{
    private const string HardwareType = "Cpu";
    private const string Device = "/cpu/0";
    private const string CoreStates = "CPU Core Performance States";

    // Mach counts each kind of tick in 32 bits, which wrap: one wrapping takes this from their sum.
    private const double TickWrap = 4294967296d;

    private readonly double[] _efficiency = Chip.Frequencies(manager, Chip.EfficiencyTable);
    private readonly double[] _performance = Chip.Frequencies(manager, Chip.PerformanceTable);

    public IEnumerable<RawSensor> Read(IMac mac, SmcValues smc, IReadOnlyList<ReportChannel> report, double? at)
    {
        var sensors = new List<RawSensor>();
        sensors.AddRange(Temperatures(smc));
        if (report.Any(channel => channel.Name.EndsWith("CPU Energy", StringComparison.Ordinal)))
        {
            sensors.Add(Sensor("power", 0, AppleSilicon.CpuPower, Chip.Watts(report, "CPU Energy", counters, at)));
        }

        var (total, loads) = Loads(mac.CpuTicks());
        sensors.Add(Sensor("load", 0, "CPU Total", total));
        sensors.AddRange(loads.Select((load, index) => Sensor("load", index + 1, Numbered(index), load)));
        var cores = report.Where(channel => channel.Subgroup == CoreStates && channel.States is not null).ToList();
        if (cores.Count > 0)
        {
            var clocks = cores.Select(core => Chip.Clock(core, Table(core.Name), counters)).ToList();
            var running = clocks.OfType<double>().ToList();
            sensors.Add(Sensor("clock", 0, "Cores (Average)", running.Count > 0 ? running.Average() : null));
            sensors.AddRange(clocks.Select((clock, index) => Sensor("clock", index + 1, Numbered(index), clock)));
        }

        return sensors;
    }

    // The cores' keys, then their hottest of all, and of each kind.
    private List<RawSensor> Temperatures(SmcValues smc)
    {
        var cores = smc.Keys.Where(found => found.Named.Role is AppleRole.PerformanceCore or AppleRole.EfficiencyCore)
            .Select(found => (found.Named, Value: BoardReader.Celsius(smc[found.Key]))).ToList();
        if (cores.Count == 0)
        {
            return [];
        }

        double? Hottest(AppleRole? role)
        {
            return cores.Where(core => role is null || core.Named.Role == role).Max(core => core.Value);
        }

        List<RawSensor> sensors =
        [
            Sensor("temperature", 0, AppleSilicon.HottestCore, Hottest(null)),
            Sensor("temperature", 1, "P-Core Max", Hottest(AppleRole.PerformanceCore)),
            Sensor("temperature", 2, "E-Core Max", Hottest(AppleRole.EfficiencyCore)),
        ];
        sensors.AddRange(cores.Select((core, index) => Sensor("temperature", index + 3, core.Named.Name, core.Value)));
        return sensors;
    }

    // Each core's share of its ticks spent busy, and the whole CPU's; none where Mach answered none.
    private (double? Total, IReadOnlyList<double?> Cores) Loads(IReadOnlyList<long[]>? ticks)
    {
        if (ticks is null || ticks.Any(cpu => cpu.Length < 4))
        {
            return (null, []);
        }

        var busy = ticks.Select((cpu, index) => counters.Change($"cpu{index}/busy", cpu[0] + cpu[1] + cpu[3], TickWrap)).ToList();
        var all = ticks.Select((cpu, index) => counters.Change($"cpu{index}/all", cpu.Take(4).Sum(), TickWrap)).ToList();
        static double? Share(double? spent, double? of)
        {
            return spent is { } time && of is > 0 ? Math.Clamp(100 * time / of.Value, 0, 100) : null;
        }

        var total = busy.All(time => time is not null) && all.All(time => time is not null) ? Share(busy.Sum(), all.Sum()) : null;
        return (total, [.. busy.Zip(all, Share)]);
    }

    // IOReport's ECPU channels are the efficiency cores', its PCPU channels the performance cores'.
    private double[] Table(string core) =>
        core.Contains("ECPU", StringComparison.Ordinal) ? _efficiency
        : core.Contains("PCPU", StringComparison.Ordinal) ? _performance
        : [];

    private static string Numbered(int index) => string.Create(CultureInfo.InvariantCulture, $"CPU Core #{index + 1}");

    private RawSensor Sensor(string type, int index, string name, double? value) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{Device}/{type}/{index}"), HardwareType, chip,
            char.ToUpperInvariant(type[0]) + type[1..], name, value);
}
