namespace Thermi.Core;

/// <summary>
/// One entry as the overlay paints it. <see cref="Label"/> is null when the previous entry has the same
/// label, so "CPU 62 °C 12 % 88 W" reads as one group. <see cref="Fill"/> is the meter, 0 to 1, or null for none.
/// <see cref="Room"/> is the widest value the item shows, the width kept for <see cref="Value"/>.
/// </summary>
public sealed record OverlayItem(string? Label, string Value, double? Fill, Level Level, string Room);

/// <summary>Turns the config and the current readings into what the overlay shows. Pixels are the painter's job.</summary>
public static class OverlayLayout
{
    public static IReadOnlyList<OverlayItem> Build(OverlayConfig config, IReadOnlyDictionary<string, Reading> readings, IFormatProvider culture)
    {
        var items = new List<OverlayItem>();
        string? previousLabel = null;
        foreach (var item in config.Items)
        {
            if (Metrics.Find(item.Metric) is not { } metric)
            {
                continue;
            }

            Reading? meter = item.Meter is not null && readings.TryGetValue(item.Meter, out var meterReading) ? meterReading : null;
            var leveled = meter ?? readings.GetValueOrDefault(metric.Id);
            var level = item.Threshold is { } threshold && leveled is { } reading ? threshold.LevelOf(reading) : Level.Normal;
            var value = ReadingFormatter.Format(readings.GetValueOrDefault(metric.Id, new Reading(metric.Kind, null)), culture);
            items.Add(new OverlayItem(metric.Label == previousLabel ? null : metric.Label, value, item.Meter is null ? null : Fill(meter), level, ReadingFormatter.Widest(metric.Kind, culture)));
            previousLabel = metric.Label;
        }

        return items;
    }

    /// <summary>The groups the overlay paints as separate pills: each label starts one, and the unlabeled items after it join it.</summary>
    public static IReadOnlyList<IReadOnlyList<OverlayItem>> Groups(IReadOnlyList<OverlayItem> items)
    {
        var groups = new List<List<OverlayItem>>();
        foreach (var item in items)
        {
            if (item.Label is not null || groups.Count == 0)
            {
                groups.Add([]);
            }

            groups[^1].Add(item);
        }

        return groups;
    }

    // Loads, duties and temperatures all read on a 0 to 100 scale. A meter without a value stays empty.
    private static double Fill(Reading? meter) => meter?.Usable is { } value ? Math.Clamp(value / 100, 0, 1) : 0;
}
