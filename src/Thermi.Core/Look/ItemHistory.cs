using Thermi.Core.Messages;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// An overlay's items over time, for what they show beyond the value now: the minimum, maximum and average since the start
/// or the last <see cref="Reset()"/>, the value smoothed over its last seconds, a line's last minute, and the ten
/// minutes of what each item warns by that the window's chart starts with. Each item keeps a minute of its value and ten of that in
/// fixed rings of one-second slots, with gaps left empty, and its running minimum, maximum and average, so memory never
/// grows with time; the service and the pipe keep none. The overlay keeps one for all its overlays from its start, so an
/// item keeps its own as overlays are deleted, merged or moved; the window keeps one too, which takes the overlay's as it
/// opens and after each reset, so each item shows in the window what it shows on the overlay.
/// </summary>
public sealed class ItemHistory(TimeProvider time)
{
    public static readonly TimeSpan Kept = TimeSpan.FromMinutes(1);

    private static readonly Reading Nothing = new(SensorKind.Load, null);

    private static readonly TimeSpan SameSecond = TimeSpan.FromSeconds(0.5);

    private readonly SensorHistory _values = new(time, Kept);
    private readonly SensorHistory _meters = new(time, SensorHistory.Kept);
    private readonly Dictionary<string, Stats> _stats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Value, string Meter)> _sources = new(StringComparer.Ordinal);

    /// <summary>
    /// Keeps what each item of every overlay reads now, each overlay with its own readings, and what it warns by: once per fresh
    /// snapshot, never for stale data. An item is kept by its id, whichever overlay holds it; items in none are forgotten. A
    /// ping item reads the settings' <paramref name="pingHost"/>.
    /// </summary>
    public void Add(IReadOnlyList<(OverlayConfig Overlay, IReadOnlyDictionary<string, Reading> Readings)> overlays, string? pingHost)
    {
        Follow(overlays.SelectMany(overlay => overlay.Overlay.Items), pingHost);
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

        foreach (var id in _sources.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _sources.Remove(id);
        }

        foreach (var item in overlays.SelectMany(overlay => overlay.Overlay.Items))
        {
            if (Smoothed(item) is { } value)
            {
                Count(item.Id, value);
            }
        }
    }

    /// <summary>As <see cref="Add(IReadOnlyList{ValueTuple{OverlayConfig, IReadOnlyDictionary{string, Reading}}}, string)"/>, with one overlay.</summary>
    public void Add(OverlayConfig overlay, IReadOnlyDictionary<string, Reading> readings, string? pingHost) => Add([(overlay, readings)], pingHost);

    /// <summary>
    /// Follows what each of <paramref name="items"/> reads: one that reads another value, or warns by another reading, than
    /// it last did, by an edit or its undo, forgets the seconds, minimum, maximum and average it kept of the one before, so
    /// no chart, graph or statistic mixes two readings; a ping item, whose host <paramref name="pingHost"/> changed, too.
    /// Gives the items whose reading they warn by changed.
    /// </summary>
    public IReadOnlyList<ItemConfig> Follow(IEnumerable<ItemConfig> items, string? pingHost)
    {
        var changed = new List<ItemConfig>();
        foreach (var item in items)
        {
            var source = SourceOf(item, pingHost);
            if (_sources.TryGetValue(item.Id, out var was) && was.Value != source.Value)
            {
                _values.Forget(item.Id);
                _stats.Remove(item.Id);
            }

            if (was.Meter is not null && was.Meter != source.Meter)
            {
                _meters.Forget(item.Id);
                changed.Add(item);
            }

            _sources[item.Id] = source;
        }

        return changed;
    }

    /// <summary>
    /// Starts again what an item warns by from <paramref name="values"/>, a value a second, oldest first and ending now, as
    /// read elsewhere.
    /// </summary>
    public void Refill(string id, IReadOnlyList<double?> values) =>
        _meters.Seed(id, [.. values.Select(value => value ?? double.NaN)], time.GetTimestamp());

    // What an item's value and what it warns by are read from. An offset corrects the same sensor, so its seconds stay.
    private static (string Value, string Meter) SourceOf(ItemConfig item, string? pingHost)
    {
        var sensors = item.Sensors is { } each ? string.Join(',', each) : null;
        var host = PingSensors.Reads(item) ? pingHost : null;
        var value = string.Join('|', item.Metric, item.Auto, item.Sensor, sensors, item.Combine, host);
        return (value, item.WarnBy is { } other ? string.Join('|', "by", other.Metric, other.Sensor, other.Auto, host) : value);
    }

    /// <summary>After a reset, so an open window starts its minimums, maximums and averages again with the overlay's.</summary>
    public event EventHandler? WasReset;

    /// <summary>Starts the minimum, maximum and average again, as "Reset min and max" in an overlay's menu does.</summary>
    public void Reset()
    {
        _stats.Clear();
        WasReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts the minimum, maximum and average of those overlays' items again, as their menus do.</summary>
    public void Reset(IEnumerable<OverlayConfig> overlays)
    {
        foreach (var item in overlays.SelectMany(overlay => overlay.Items))
        {
            _stats.Remove(item.Id);
        }

        WasReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the window is sent, as it opens and after a reset: the ten minutes of what every item warns by, its value's last minute and
    /// its minimum, maximum and average, ending now, for each item that has any; an item left out has none.
    /// </summary>
    public History Sent(ThermiConfig config)
    {
        var now = time.GetTimestamp();
        var items = new List<Messages.ItemHistory>();
        foreach (var item in config.Overlays.SelectMany(overlay => overlay.Items))
        {
            var meter = TenMinutes(item.Id);
            var stats = _stats.GetValueOrDefault(item.Id);
            if (stats is not null || meter.Any(value => value is not null))
            {
                items.Add(new Messages.ItemHistory(item.Id, now, SlotsOf(meter))
                {
                    Values = [.. _values.LastMinute(item.Id).Select(value => value ?? double.NaN)],
                    Stats = stats?.Sent,
                });
            }
        }

        return new History(items);
    }

    /// <summary>
    /// Takes what the overlay sent: each item's minimum, maximum and average are the overlay's from here on, none after its
    /// reset or where it sent none of the item, and its value's last minute and the ten of what it warns by fill the seconds
    /// this history did not read, so smoothing and the chart start where they are.
    /// </summary>
    public void Take(History history)
    {
        var sentIds = history.Items.Select(sent => sent.ItemId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _stats.Keys.Where(id => !sentIds.Contains(id)).ToList())
        {
            _stats.Remove(id);
        }

        foreach (var sent in history.Items)
        {
            _values.Seed(sent.ItemId, sent.Values, sent.Newest);
            _meters.Seed(sent.ItemId, [.. sent.Slots.Select(slot => (double)slot)], sent.Newest);
            if (sent.Stats is { } stats)
            {
                _stats[sent.ItemId] = new Stats(stats);
            }
            else
            {
                _stats.Remove(sent.ItemId);
            }
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

    /// <summary>What the item warns by over its last minute, a value a second and oldest first, each colored by its lines; null where it was missing.</summary>
    public IReadOnlyList<GraphPoint?> Graph(ItemConfig item) => GraphOf(item, _meters.LastMinute(item.Id));

    /// <summary>
    /// The line of <see cref="Graph"/> for the last minute of <paramref name="meters"/>, what the item warns by a second
    /// and oldest first, as the window draws the minute the overlay sent it.
    /// </summary>
    public static IReadOnlyList<GraphPoint?> GraphOf(ItemConfig item, IReadOnlyList<double?> meters)
    {
        // A fan or throughput fills on the window chart's range, not up to its possible maximum, where a line lies flat; the
        // rest on the overlay's own scale.
        var kind = Threshold.JudgedKind(item) ?? SensorKind.Load;
        var minute = meters.Skip(Math.Max(0, meters.Count - (int)Kept.TotalSeconds)).ToList();
        var top = kind is SensorKind.Fan or SensorKind.Throughput
            ? MeterChart.Range(new MeterScale(kind, item.Threshold?.Warning, item.Threshold?.Critical, 1, 1), [.. minute.OfType<double>()]).To
            : (double?)null;
        return [.. minute.Select(value => value is { } read
            ? new GraphPoint(
                top is { } range ? Math.Clamp(read / range, 0, 1) : OverlayLayout.FillOf(read, kind),
                item.Threshold?.LevelOf(new Reading(kind, read)) ?? Level.Normal)
            : (GraphPoint?)null)];
    }

    /// <summary>
    /// What an item warns by over the last ten minutes, its own reading unless it warns by another, a value a second and
    /// oldest first, from its first reading on; null where it was missing.
    /// </summary>
    public IReadOnlyList<double?> TenMinutes(string itemId) => _meters.Last(itemId, (int)SensorHistory.Kept.TotalSeconds);

    private double? Smoothed(ItemConfig item)
    {
        var values = _values.Last(item.Id, Math.Max(1, item.Smooth)).OfType<double>().ToList();
        return values.Count == 0 ? null : values.Average();
    }

    // A value within half a second of the last one counted is that second's, as SensorHistory takes it: the overlay sends
    // what it counted of the snapshot the window then reads too, which counts it once.
    private void Count(string id, double value)
    {
        if (!_stats.TryGetValue(id, out var stat))
        {
            _stats[id] = stat = new Stats();
        }
        else if (time.GetElapsedTime(stat.Counted) < SameSecond)
        {
            return;
        }

        stat.Add(value, time.GetTimestamp());
    }

    // The newest value in the last slot, the slots before the first kept value NaN.
    private static float[] SlotsOf(IReadOnlyList<double?> values)
    {
        var slots = new float[(int)SensorHistory.Kept.TotalSeconds];
        Array.Fill(slots, float.NaN);
        for (var at = 0; at < values.Count; at++)
        {
            slots[slots.Length - values.Count + at] = values[at] is { } value ? (float)value : float.NaN;
        }

        return slots;
    }

    private sealed class Stats()
    {
        private double _sum;
        private long _count;

        public Stats(ItemStats sent)
            : this()
        {
            (Min, Max, _sum, _count, Counted) = (sent.Min, sent.Max, sent.Sum, sent.Count, sent.Counted);
        }

        public double Min { get; private set; } = double.PositiveInfinity;

        public double Max { get; private set; } = double.NegativeInfinity;

        public double Average => _sum / _count;

        public long Counted { get; private set; }

        public ItemStats Sent => new(Min, Max, _sum, _count, Counted);

        public void Add(double value, long now)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            _sum += value;
            _count++;
            Counted = now;
        }
    }
}
