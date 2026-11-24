using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Look;

/// <summary>
/// One entry as the overlay paints it. <see cref="Label"/> is null when the entry shows no label, as when it continues
/// the run of the one before, so "CPU 62 °C 12 % 88 W" reads as one group. <see cref="Level"/> is what its meter's
/// thresholds say, which the value shows by its mark. <see cref="Room"/> is the widest value the item shows, the width
/// kept for <see cref="Value"/>.
/// </summary>
public sealed record OverlayItem(string? Label, string Value, Level Level, string Room)
{
    /// <summary>The id of the item it shows, so the stage can tell which item was clicked; empty for what is no item.</summary>
    public string Id { get; init; } = "";

    /// <summary>The value's own color as #RRGGBB, or null for the style's.</summary>
    public string? Color { get; init; }

    /// <summary>
    /// A graph meter's last minute, a value a second and oldest first, null where one was missing;
    /// null without a graph.
    /// </summary>
    public IReadOnlyList<GraphPoint?>? Graph { get; init; }

    /// <summary>
    /// How many of its meter's <see cref="OverlayLayout.Segments"/> are lit from the bottom, in its level's color; null for
    /// an item without a meter or with its graph, which draws its meter in its place.
    /// </summary>
    public int? Lit { get; init; }

    /// <summary>What <see cref="Value"/> is: the value now, or a minimum, maximum or average, which the stage marks as since the window opened.</summary>
    public ItemStat Stat { get; init; }

    /// <summary>Whether the value is marked as the CPU throttling, a dashed line over it, at any level.</summary>
    public bool Throttled { get; init; }
}

/// <summary>One second of a graph meter: its fill, 0 to 1, and the level the item's thresholds give it.</summary>
public readonly record struct GraphPoint(double Fill, Level Level);

/// <summary>Turns an overlay's settings and the current readings into what it shows. Pixels are the painter's job.</summary>
public static class OverlayLayout
{
    public const int Segments = 5;

    /// <summary>
    /// What the overlay shows: numbers in <paramref name="culture"/>, the regional format, default labels in the language of
    /// <paramref name="texts"/>, English when there are none, and temperatures in the chosen unit. With a
    /// <paramref name="history"/>, items show their minimum, maximum, average or smoothed value and graphs their last
    /// minute; without one, the value now. An item shown only on alert is left out while its meter is normal, unless the
    /// window is open, so every item can be selected.
    /// </summary>
    public static IReadOnlyList<OverlayItem> Build(
        OverlayConfig overlay,
        IReadOnlyDictionary<string, Reading> readings,
        IFormatProvider culture,
        Texts? texts = null,
        TemperatureUnit temperature = TemperatureUnit.Celsius,
        ItemHistory? history = null,
        bool windowOpen = false)
    {
        var items = new List<OverlayItem>();
        var labels = RunLabels(overlay.Items, texts);
        string? shownLabel = null;
        for (var index = 0; index < overlay.Items.Count; index++)
        {
            var item = overlay.Items[index];
            var reading = ReadingOf(item, readings);
            if (Text(item, history?.Shown(item, reading) ?? reading, culture, overlay.Style.Units, temperature, texts) is not var (value, room))
            {
                continue;
            }

            var meter = MeterOf(item, readings);
            var level = LevelOf(item, meter);
            var label = labels[index];
            if (item.AlertOnly && level == Level.Normal && !windowOpen)
            {
                continue;
            }

            var labeled = Labeled(overlay.Style.Layout, item, label != shownLabel);
            items.Add(new OverlayItem(labeled ? label : null, value, level, room)
            {
                Id = item.Id,
                Color = item.Color,
                Graph = GraphOf(item, meter, history),
                Lit = LitOf(item, meter),
                Stat = item.Stat,
            });
            shownLabel = label;
        }

        return items;
    }

    /// <summary>
    /// The label an item shows: its own, or its metric's in the language of <paramref name="texts"/>.
    /// A sensor item without one joins the pill before it.
    /// </summary>
    public static string? LabelOf(ItemConfig item, Texts? texts = null) =>
        item.Label ?? (Metrics.Find(item.Metric ?? item.Auto) is { } metric ? (texts ?? Texts.English).Of(metric.Label) : null);

    /// <summary>Each item's label as the overlay shows it, as <see cref="LabelOf"/> gives it, an item without one taking the label before it.</summary>
    public static IReadOnlyList<string?> RunLabels(IReadOnlyList<ItemConfig> items, Texts? texts = null)
    {
        var labels = new List<string?>(items.Count);
        string? run = null;
        foreach (var item in items)
        {
            run = LabelOf(item, texts) ?? run;
            labels.Add(run);
        }

        return labels;
    }

    /// <summary>
    /// What an item reads now, its sensors combined and its offset added, or null when it names sensors the snapshot lacks.
    /// A combined item leaves out its sensors without a value, and is missing when none is left.
    /// </summary>
    internal static Reading? ReadingOf(ItemConfig item, IReadOnlyDictionary<string, Reading> readings) =>
        SourceOf(item, readings) is { } reading
            ? reading with { Value = reading.Usable + item.Offset, Bits = item.Bits && reading.Kind == SensorKind.Throughput }
            : null;

    /// <summary>
    /// What an item's meter reads, or null when it has none or its sensor is gone. An item metered by itself meters the
    /// value it shows, offset included, so its thresholds color what it reads.
    /// </summary>
    internal static Reading? MeterOf(ItemConfig item, IReadOnlyDictionary<string, Reading> readings) =>
        item.Meter is not { } meter ? null
        : MetersItself(item) ? ReadingOf(item, readings)
        : meter.Sensor is { } sensor ? Named(sensor, meter.Auto, readings)
        : readings.GetValueOrDefault(meter.Metric ?? "");

    /// <summary>True when the item's meter is the item's own value, its metric or its sensor.</summary>
    public static bool MetersItself(ItemConfig item) =>
        item.Meter is { } meter && ((meter.Metric is not null && meter.Metric == item.Metric) || (meter.Sensor is not null && meter.Sensor == item.Sensor));

    /// <summary>
    /// The level its thresholds give its meter: normal without a meter, thresholds or a value. An item metered by itself
    /// is judged as it shows, with its own decimals.
    /// </summary>
    internal static Level LevelOf(ItemConfig item, Reading? meter) =>
        item is { Meter: not null, Threshold: { } threshold } && meter is { } reading
            ? threshold.LevelOf(reading, MetersItself(item) ? item.Decimals : null)
            : Level.Normal;

    /// <summary>
    /// The groups the overlay paints as separate pills. With pills, each label starts one, and the unlabeled items after
    /// it join it; each item is a pill of its own with each, and all of them make one bar with one. Without
    /// <paramref name="labels"/>, the pills stay as they are and show no label.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<OverlayItem>> Groups(IReadOnlyList<OverlayItem> items, PillLayout layout = PillLayout.Pills, bool labels = true)
    {
        var groups = new List<List<OverlayItem>>();
        foreach (var item in items)
        {
            if (groups.Count == 0 || layout == PillLayout.Each || (layout == PillLayout.Pills && item.Label is not null))
            {
                groups.Add([]);
            }

            groups[^1].Add(labels ? item : item with { Label = null });
        }

        return groups;
    }

    /// <summary>Loads, duties and temperatures all read on a 0 to 100 scale, a fan's speed on the range of its lines.</summary>
    internal static double FillOf(double value, SensorKind kind = SensorKind.Load) => Math.Clamp(value / Threshold.MaxOf(kind), 0, 1);

    // A sensor or combined item is read from its sensors even when it keeps a metric as auto: two items that keep the
    // same one show their own.
    private static Reading? SourceOf(ItemConfig item, IReadOnlyDictionary<string, Reading> readings) =>
        item.Sensors is { } sensors ? Combined(sensors, item.Combine ?? Combine.Max, readings)
        : item.Sensor is { } sensor ? Named(sensor, item.Auto, readings)
        : Metrics.Find(item.Metric) is { } metric ? readings.GetValueOrDefault(metric.Id, new Reading(metric.Kind, null))
        : null;

    // A named sensor that is gone, or of another type than the metric it keeps as auto, shows as that metric's missing value.
    private static Reading? Named(string sensor, string? auto, IReadOnlyDictionary<string, Reading> readings)
    {
        var metric = Metrics.Find(auto);
        return readings.TryGetValue(sensor, out var reading) && (metric is null || reading.Kind == metric.Kind) ? reading
            : metric is null ? null
            : new Reading(metric.Kind, null);
    }

    private static Reading? Combined(IReadOnlyList<string> sensors, Combine combine, IReadOnlyDictionary<string, Reading> readings)
    {
        var found = sensors.Where(readings.ContainsKey).Select(sensor => readings[sensor]).ToList();
        if (found.Count == 0)
        {
            return null;
        }

        var values = found.Select(reading => reading.Usable).OfType<double>().ToList();
        double? value = values.Count == 0 ? null : combine switch
        {
            Combine.Average => values.Average(),
            Combine.Sum => values.Sum(),
            Combine.Max => values.Max(),
            _ => values.Max(),
        };
        return new Reading(found[0].Kind, value);
    }

    private static bool Labeled(PillLayout layout, ItemConfig item, bool newLabel) =>
        layout == PillLayout.Each || newLabel || (layout == PillLayout.Pills && item.Own);

    // Without a history, a graph has only the value now.
    private static IReadOnlyList<GraphPoint?>? GraphOf(ItemConfig item, Reading? meter, ItemHistory? history) =>
        !item.Graph || item.Meter is null ? null
        : history is not null ? history.Graph(item)
        : [meter is { Usable: { } value } read ? new GraphPoint(FillOf(value, read.Kind), LevelOf(item, meter)) : null];

    // Any value above zero lights a segment, so a meter that shows none reads as zero or nothing read.
    private static int? LitOf(ItemConfig item, Reading? meter)
    {
        if (item.Graph || item.Meter is null)
        {
            return null;
        }

        var fill = meter is { Usable: { } value } read ? FillOf(value, read.Kind) : 0;
        return fill > 0 ? Math.Max(1, (int)Math.Round(fill * Segments, MidpointRounding.AwayFromZero)) : 0;
    }

    // A sensor the snapshot lacks has no known unit, so it keeps only the room of the missing mark. A network's speed shows
    // which way it goes by an arrow before it, the same in every language, so a pill reads "NET ↓ 12.4 MB/s ↑ 1.2 MB/s".
    // An address changes only with the network, so it keeps the room of the one it shows, not of 255.255.255.255.
    private static (string Value, string Room)? Text(
        ItemConfig item,
        Reading? reading,
        IFormatProvider culture,
        bool units,
        TemperatureUnit temperature,
        Texts? texts)
    {
        if (reading is not { } read)
        {
            return item.Sensor is null && item.Sensors is null ? null : (ReadingFormatter.Missing, ReadingFormatter.Missing);
        }

        var arrow = read.Direction == Direction.Down ? "↓ " : read.Direction == Direction.Up ? "↑ " : "";
        var value = arrow + ReadingFormatter.Format(read, culture, units, temperature, item.Decimals, texts);
        return (
            value,
            read.Kind == SensorKind.Address
                ? value
                : arrow + ReadingFormatter.Widest(read.Kind, culture, units, temperature, item.Decimals, texts));
    }
}
