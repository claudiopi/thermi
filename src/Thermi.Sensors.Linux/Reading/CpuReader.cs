using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// The CPU, as one device: its chips' temperatures, its load from <c>/proc/stat</c>, in total and for each CPU the kernel
/// counts, its clocks from cpufreq, or from <c>/proc/cpuinfo</c> where there is none, as in a virtual machine, and its
/// power from RAPL's energy counters, which the kernel lets only root and the service's group read: to anyone else, the
/// power reads missing.
/// </summary>
internal sealed class CpuReader
{
    private const string HardwareType = "Cpu";
    private const string Device = "/cpu/0";
    private const string Powercap = "/sys/class/powercap";

    private readonly string _model;
    private readonly IReadOnlyList<Chip> _chips;
    private readonly IReadOnlyList<string> _cpus;
    private readonly bool _clocks;
    private readonly IReadOnlyList<double?> _infoClocks;
    private readonly IReadOnlyList<(string Zone, string Name, double Wrap)> _zones;
    private readonly int _chipPowers;
    private readonly Counters _counters;

    /// <summary>The CPU as the kernel shows it this second, with the counters its loads and power keep across seconds.</summary>
    public CpuReader(IKernel kernel, IReadOnlyList<Chip> chips, Counters counters)
    {
        _chips = Hwmon.NumberedOn(chips);
        _counters = counters;
        var cpuinfo = kernel.Read("/proc/cpuinfo");
        _model = Model(cpuinfo);
        _cpus = [.. Stat(kernel).Keys.Where(cpu => cpu != "cpu")];
        _clocks = _cpus.Count > 0 && kernel.Read(Frequency(_cpus[0])) is not null;
        _infoClocks = _clocks ? [] : InfoClocks(cpuinfo);
        _zones = [.. kernel.List(Powercap).Where(zone => zone.StartsWith("intel-rapl:", StringComparison.Ordinal))
            .Select(zone => (Zone: $"{Powercap}/{zone}", Name: kernel.Read($"{Powercap}/{zone}/name")?.Trim() ?? zone))
            .Select(zone => (zone.Zone, zone.Name, Wrap: Counters.Number(kernel.Read($"{zone.Zone}/max_energy_range_uj")) ?? 0))];
        _chipPowers = chips.Sum(chip => chip.Inputs.Count(input => input.Type == "Power"));
    }

    public IEnumerable<RawSensor> Read(IKernel kernel, double? at)
    {
        var sensors = new List<RawSensor>();
        foreach (var chip in _chips)
        {
            sensors.AddRange(Hwmon.Read(kernel, chip, Device, HardwareType, _model));
        }

        var stat = Stat(kernel);
        sensors.Add(Sensor("load", 0, "CPU Total", Load("cpu", stat)));
        sensors.AddRange(_cpus.Select((cpu, index) => Sensor("load", index + 1, Numbered(index), Load(cpu, stat))));
        var clocks = _clocks ? [.. _cpus.Select(cpu => Counters.Number(kernel.Read(Frequency(cpu))) / 1000)] : _infoClocks;
        if (clocks.Count > 0)
        {
            sensors.Add(Sensor("clock", 0, "Cores (Average)", clocks.All(clock => clock is not null) ? clocks.Average() : null));
            sensors.AddRange(clocks.Select((clock, index) => Sensor("clock", index + 1, Numbered(index), clock)));
        }

        // Microjoules over seconds: watts, across a counter's wrap.
        sensors.AddRange(_zones.Select((zone, index) => Sensor(
            "power",
            _chipPowers + index,
            zone.Name,
            _counters.Rate(zone.Zone, Counters.Number(kernel.Read($"{zone.Zone}/energy_uj")), at, zone.Wrap) / 1e6)));
        return sensors;
    }

    // The first CPU's model, as the kernel names it; an Arm CPU's /proc/cpuinfo names none.
    private static string Model(string? cpuinfo) =>
        cpuinfo?.Split('\n')
            .Select(line => line.Split(':', 2))
            .FirstOrDefault(pair => pair is [var key, _] && key.Trim() == "model name") is [_, var model]
            ? model.Trim()
            : "CPU";

    // Each processor's "cpu MHz", which x86's kernel gives without cpufreq; an Arm CPU's gives none.
    private static List<double?> InfoClocks(string? cpuinfo) =>
        [.. (cpuinfo ?? "").Split('\n')
            .Select(line => line.Split(':', 2))
            .Where(pair => pair is [var key, _] && key.Trim() == "cpu MHz")
            .Select(pair => double.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) ? mhz : (double?)null)];

    private static string Numbered(int index) => string.Create(CultureInfo.InvariantCulture, $"CPU #{index + 1}");

    private static string Frequency(string cpu) => $"/sys/devices/system/cpu/{cpu}/cpufreq/scaling_cur_freq";

    // Each CPU's counters of time, in the kernel's ticks: what it spent busy, and all of it.
    private static Dictionary<string, (double Busy, double All)> Stat(IKernel kernel)
    {
        var times = new Dictionary<string, (double Busy, double All)>(StringComparer.Ordinal);
        foreach (var fields in (kernel.Read("/proc/stat") ?? "").Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            // user, nice, system, idle, iowait, irq, softirq and steal; a guest's time is in user's already.
            if (fields is [var name, ..] && name.StartsWith("cpu", StringComparison.Ordinal) && fields.Length >= 9
                && fields[1..9].Select(Counters.Number).ToList() is var counts && counts.TrueForAll(count => count is not null))
            {
                var all = counts.Sum(count => count!.Value);
                times[name] = (all - counts[3]!.Value - counts[4]!.Value, all);
            }
        }

        return times;
    }

    private double? Load(string cpu, Dictionary<string, (double Busy, double All)> stat)
    {
        var found = stat.TryGetValue(cpu, out var times);
        var busy = _counters.Change($"{cpu}/busy", found ? times.Busy : null);
        var all = _counters.Change($"{cpu}/all", found ? times.All : null);
        return busy is { } spent && all is > 0 ? 100 * spent / all : null;
    }

    private RawSensor Sensor(string type, int index, string name, double? value) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{Device}/{type}/{index}"), HardwareType, _model, Capitalized(type), name, value);

    private static string Capitalized(string type) => char.ToUpperInvariant(type[0]) + type[1..];
}
