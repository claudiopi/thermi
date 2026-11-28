using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// What IOReport's counters of the chip say over a second: the watts a part used, from its energy channel, and the clock a
/// core ran at, from the time it spent in each performance state and the frequency of each, which the power manager's
/// DVFS tables in the registry hold.
/// </summary>
internal static class Chip
{
    /// <summary>The registry's class of the power manager, <c>pmgr</c>, which holds the DVFS tables.</summary>
    public const string PowerManager = "AppleARMIODevice";

    /// <summary>The efficiency and the performance cores' tables, as M1 to M4 keep them.</summary>
    public const string EfficiencyTable = "voltage-states1-sram";

    public const string PerformanceTable = "voltage-states5-sram";

    private const string Energy = "Energy Model";

    // A table's entries, a frequency then a voltage, each a 32-bit number; M1 to M3 keep hertz, M4 on kilohertz.
    private const int EntrySize = 8;
    private const double KilohertzAtMost = 1e8;

    // States a core spends no clock in, which come before its frequencies.
    private static readonly string[] Resting = ["IDLE", "OFF", "DOWN"];

    /// <summary>
    /// The watts of the energy channels named <paramref name="channel"/>, summed, as an Ultra's two dies each have one, over
    /// the second; null where the Mac has none or the unit is none of IOReport's.
    /// </summary>
    public static double? Watts(IReadOnlyList<ReportChannel> report, string channel, Counters counters, double? at)
    {
        var parts = report.Where(each => each.Group == Energy && each.Name.EndsWith(channel, StringComparison.Ordinal)).ToList();
        double? joules = parts.Count == 0 ? null : 0;
        foreach (var part in parts)
        {
            joules += part.Value / PerJoule(part.Unit);
        }

        return counters.Rate(channel, joules, at);
    }

    /// <summary>A DVFS table's frequencies in megahertz, from the power manager's property <paramref name="table"/>; none where it has none.</summary>
    public static double[] Frequencies(IReadOnlyList<JsonObject> manager, string table)
    {
        var data = manager.Select(entry => MacJson.Text(entry[table])).FirstOrDefault(value => value is not null);
        if (data is null)
        {
            return [];
        }

        var bytes = new byte[data.Length];
        if (!Convert.TryFromBase64String(data, bytes, out var length))
        {
            return [];
        }

        bytes = bytes[..length];
        var hertz = Enumerable.Range(0, bytes.Length / EntrySize)
            .Select(index => (double)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * EntrySize))).ToArray();
        var scale = hertz.Length > 0 && hertz.Max() > KilohertzAtMost ? 1e6 : 1e3;
        return [.. hertz.Select(frequency => frequency / scale)];
    }

    /// <summary>
    /// The clock a core ran at over the second, in megahertz: each state's frequency, weighted by the time spent in it; null
    /// where it rested all the second, or its states do not line up with the table.
    /// </summary>
    public static double? Clock(ReportChannel core, double[] frequencies, Counters counters)
    {
        var active = (core.States ?? []).SkipWhile(state => Resting.Contains(state.Name, StringComparer.Ordinal)).ToList();
        if (active.Count == 0 || active.Count > frequencies.Length)
        {
            return null;
        }

        // A table may start with a state the channel counts as resting, so they line up from their ends.
        var offset = frequencies.Length - active.Count;
        double? spent = 0;
        double? weighted = 0;
        foreach (var (state, index) in active.Select((state, index) => (state, index)))
        {
            var time = counters.Change($"{core.Name}/{state.Name}", state.Residency);
            spent += time;
            weighted += time * frequencies[offset + index];
        }

        return spent is > 0 ? weighted / spent : null;
    }

    private static double? PerJoule(string? unit) => unit switch
    {
        "mJ" => 1e3,
        "uJ" => 1e6,
        "nJ" => 1e9,
        _ => null,
    };
}
