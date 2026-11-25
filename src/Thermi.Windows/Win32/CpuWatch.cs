using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Thermi.Core.Tray;
using Windows.Win32;
using Windows.Win32.System.Performance;

namespace Thermi.Windows.Win32;

/// <summary>
/// What the CPU's and the GPU's tooltips say of them beyond the sensors: the CPU's base clock, read once from what Windows
/// found at boot, and what is busiest on each, read from Windows' performance counters, which need no admin rights.
/// </summary>
internal sealed class CpuWatch : IDisposable
{
    private const string Processes = @"\Process(*)\% Processor Time";
    private const string GpuEngines = @"\GPU Engine(*)\Utilization Percentage";

    private readonly Lock _gate = new();
    private PdhQuery? _query;
    private bool _wanted;
    private bool _started;
    private int _samples;
    private Task _reading = Task.CompletedTask;
    private volatile Busiest? _busiest;

    /// <summary>The CPU's base clock in MHz, or null where Windows does not say it.</summary>
    public double? BaseClock { get; } = ReadBaseClock();

    /// <summary>The busiest process on the CPU and on the GPU, each over the last 2 s; null while not read.</summary>
    public Busiest? Busiest => _busiest;

    /// <summary>
    /// Called once a second: while <paramref name="wanted"/>, the counters are read every other call, about 12 ms of CPU each,
    /// and the busiest found once two readings make a rate; else they are closed, so they cost nothing while no value is critical.
    /// </summary>
    public void Sample(bool wanted)
    {
        if (!wanted)
        {
            Dispose();
            return;
        }

        // Read on the pool, never on the window's thread, whose paints its 12 ms would hold up.
        lock (_gate)
        {
            _wanted = true;
        }

        if (++_samples % 2 == 1 && _reading.IsCompleted)
        {
            _reading = Task.Run(Read);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _wanted = false;
            _started = false;
            _query?.Dispose();
            _query = null;
            _busiest = null;
        }

        _samples = 0;
    }

    // Each read spans the 2 s since the last; the first only sets where the rate starts from.
    private void Read()
    {
        lock (_gate)
        {
            if (!_wanted)
            {
                return;
            }

            _query ??= PdhQuery.Open(Processes, GpuEngines);
            if (_query is not { } query || !query.Collect())
            {
                return;
            }

            if (_started)
            {
                _busiest = new Busiest(BusiestCpu(query.Values(0)), BusiestGpu(query.Values(1)));
            }

            _started = true;
        }
    }

    // The "~MHz" Windows writes at boot for the first logical processor, the CPU's nominal speed.
    internal static double? ReadBaseClock()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("~MHz") is int mhz && mhz > 0 ? mhz : null;
    }

    // A process's instances are "chrome", then "chrome" with a number after a hash, each counted over every logical processor.
    private static BusyProcess? BusiestCpu(List<(string Name, double Value)> values) =>
        values
            .Where(value => value.Name is not "_Total" and not "Idle")
            .GroupBy(value => value.Name.Split('#')[0], StringComparer.OrdinalIgnoreCase)
            .Select(group => new BusyProcess(group.Key, group.Sum(value => value.Value) / Environment.ProcessorCount))
            .Where(process => process.Share >= 1)
            .MaxBy(process => process.Share);

    // An engine is "pid_1234_luid_…_engtype_3D": a process's share is that of its busiest kind of engine, as Task Manager's.
    private static BusyProcess? BusiestGpu(List<(string Name, double Value)> values) =>
        values
            .Select(value => (Pid: Field(value.Name, "pid_"), Type: Field(value.Name, "engtype_"), value.Value))
            .Where(engine => engine.Pid is not null)
            .GroupBy(engine => (engine.Pid, engine.Type))
            .Select(type => (type.Key.Pid, Share: type.Sum(engine => engine.Value)))
            .GroupBy(type => type.Pid)
            .Select(process => (Pid: process.Key!, Share: process.Max(type => type.Share)))
            .Where(process => process.Share >= 1)
            .OrderByDescending(process => process.Share)
            .Select(process => NameOf(process.Pid) is { } name ? new BusyProcess(name, process.Share) : null)
            .FirstOrDefault(process => process is not null);

    private static string? Field(string name, string prefix)
    {
        var at = name.IndexOf(prefix, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var start = at + prefix.Length;
        var end = name.IndexOf('_', start);
        return end < 0 ? name[start..] : name[start..end];
    }

    private static string? NameOf(string pid)
    {
        try
        {
            using var process = Process.GetProcessById(int.Parse(pid, CultureInfo.InvariantCulture));
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            // Gone since the counters were read.
            return null;
        }
    }

    /// <summary>A query of Windows' performance counters, each a wildcard of instances, read as doubles.</summary>
    private sealed class PdhQuery(PDH_HQUERY query, PDH_HCOUNTER[] counters) : IDisposable
    {
        // Doubles, PDH_FMT_DOUBLE, never capped at 100, PDH_FMT_NOCAP100, which Windows' metadata lacks: a process's rate
        // sums its threads over every logical processor, so it passes 100.
        private const PDH_FMT Doubles = (PDH_FMT)0x8200;

        public static unsafe PdhQuery? Open(params string[] paths)
        {
            PDH_HQUERY query = default;
            if (PInvoke.PdhOpenQuery(default, 0, &query) != 0)
            {
                return null;
            }

            var counters = new PDH_HCOUNTER[paths.Length];
            for (var at = 0; at < paths.Length; at++)
            {
                fixed (char* path = paths[at])
                fixed (PDH_HCOUNTER* counter = &counters[at])
                {
                    // A counter Windows lacks, as GPU engines without a GPU driver that has them, reads as no instance.
                    _ = PInvoke.PdhAddEnglishCounter(query, path, 0, counter);
                }
            }

            return new PdhQuery(query, counters);
        }

        public bool Collect() => PInvoke.PdhCollectQueryData(query) == 0;

        public unsafe List<(string Name, double Value)> Values(int counter)
        {
            uint size = 0;
            uint count = 0;
            if (counters[counter] == default || PInvoke.PdhGetFormattedCounterArray(counters[counter], Doubles, &size, &count, null) != PInvoke.PDH_MORE_DATA)
            {
                return [];
            }

            var buffer = new byte[size];
            fixed (byte* start = buffer)
            {
                var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)start;
                if (PInvoke.PdhGetFormattedCounterArray(counters[counter], Doubles, &size, &count, items) != 0)
                {
                    return [];
                }

                var values = new List<(string, double)>((int)count);
                for (var at = 0; at < count; at++)
                {
                    values.Add((Marshal.PtrToStringUni((nint)items[at].szName.Value) ?? "", items[at].FmtValue.Anonymous.doubleValue));
                }

                return values;
            }
        }

        public void Dispose() => _ = PInvoke.PdhCloseQuery(query);
    }
}
