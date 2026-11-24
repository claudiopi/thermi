using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// An overlay's items over time, for what they show beyond the value now: the minimum, maximum and average since the start
/// or the last <see cref="Reset()"/>, the value smoothed over its last seconds, a graph meter's last minute, and the ten
/// minutes of each meter the window's chart starts with. Each item keeps a minute of its value and ten of its meter in
/// fixed rings of one-second slots, with gaps left empty, and its running minimum, maximum and average, so memory never
/// grows with time; the service and the pipe keep none. The overlay keeps one for all its overlays from its start, so an
/// item keeps its own as overlays are deleted, merged or moved, and the window one of its own from when it opens.
/// </summary>
public sealed class ItemHistory(TimeProvider time)
{
    public static readonly TimeSpan Kept = TimeSpan.FromMinutes(1);

    private static readonly Reading Nothing = new(SensorKind.Load, null);

    private readonly SensorHistory _values = new(time, Kept);
    private readonly SensorHistory _meters = new(time, SensorHistory.Kept);
    private readonly Dictionary<string, Stats> _stats = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps what each item of every overlay reads now, each overlay with its own readings, and its meter: once per fresh
    /// snapshot, never for stale data. An item is kept by its id, whichever overlay holds it; items in none are forgotten.
    /// </summary>
    public void Add(IReadOnlyList<(OverlayConfig Overlay, IReadOnlyDictionary<string, Reading> Readings)> overlays)
    {
        var values = new Dictionary<string, Reading>(StringComparer.Ordinal);
        var meters = new Dictionary<string, Reading>(StringComparer.Ordinal);
        foreach (var (overlay, readings) in overlays)
        {
            foreach (var item in overlay.Items)
            {
                values[item.Id] = OverlayLayout.ReadingOf(item, readings) ?? Nothing;
                meters[item.Id] = OverlayLayout.MeterOf(item, readings) ?? Nothing;
            }
        }

        _values.Add(values);
        _meters.Add(meters);
        var ids = values.Keys.ToHashSet(StringComparer.Ordinal);
        _values.Keep(ids);
        _meters.Keep(ids);
        foreach (var id in _stats.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _stats.Remove(id);
        }

        foreach (var item in overlays.SelectMany(overlay => overlay.Overlay.Items))
        {
            if (Smoothed(item) is { } value)
            {
                Count(item.Id, value);
            }
        }
    }

    /// <summary>As <see cref="Add(IReadOnlyList{ValueTuple{OverlayConfig, IReadOnlyDictionary{string, Reading}}})"/>, with one overlay.</summary>
    public void Add(OverlayConfig overlay, IReadOnlyDictionary<string, Reading> readings) => Add([(overlay, readings)]);

    /// <summary>Starts the minimum, maximum and average again, as "Reset min and max" in an overlay's menu does.</summary>
    public void Reset() => _stats.Clear();

    /// <summary>Starts the minimum, maximum and average of that overlay's items again, as its own menu does.</summary>
    public void Reset(OverlayConfig overlay)
    {
        foreach (var item in overlay.Items)
        {
            _stats.Remove(item.Id);
        }
    }

    /// <summary>
    /// What an item shows, from what it reads <paramref name="now"/>: smoothed over its last seconds, or its minimum,
    /// maximum or average of those values, missing ones left out. With nothing since the start or the reset, a minimum,
    /// maximum or average is the value now; smoothing with no value in its span is missing.
    /// </summary>
    public Reading? Shown(ItemConfig item, Reading? now)
    {
        if (now is not { } reading)
        {
            return null;
        }

        var smoothed = item.Smooth == 0 ? reading.Usable : Smoothed(item);
        var stat = _stats.GetValueOrDefault(item.Id);
        return reading with
        {
            Value = item.Stat switch
            {
                ItemStat.Min => stat?.Min ?? smoothed,
                ItemStat.Max => stat?.Max ?? smoothed,
                ItemStat.Average => stat?.Average ?? smoothed,
                ItemStat.Now => smoothed,
                _ => smoothed,
            },
        };
    }

    /// <summary>The meter's last minute, a value a second and oldest first, each colored by the item's thresholds; null where it was missing.</summary>
    public IReadOnlyList<GraphPoint?> Graph(ItemConfig item) =>
        [.. _meters.LastMinute(item.Id).Select(value => value is { } read ? new GraphPoint(OverlayLayout.FillOf(read), item.Threshold?.LevelOf(new Reading(SensorKind.Load, read)) ?? Level.Normal) : (GraphPoint?)null)];

    /// <summary>An item's meter over the last ten minutes, a value a second and oldest first, from its first reading on; null where it was missing.</summary>
    public IReadOnlyList<double?> TenMinutes(string itemId) => _meters.Last(itemId, (int)SensorHistory.Kept.TotalSeconds);

    private double? Smoothed(ItemConfig item)
    {
        var values = _values.Last(item.Id, Math.Max(1, item.Smooth)).OfType<double>().ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private void Count(string id, double value)
    {
        if (!_stats.TryGetValue(id, out var stat))
        {
            _stats[id] = stat = new Stats();
        }

        stat.Add(value);
    }

    private sealed class Stats
    {
        private double _sum;
        private long _count;

        public double Min { get; private set; } = double.PositiveInfinity;

        public double Max { get; private set; } = double.NegativeInfinity;

        public double Average => _sum / _count;

        public void Add(double value)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            _sum += value;
            _count++;
        }
    }
}
