using Thermi.Core.Frames;
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
    /// What the value is when it is not the value now, as "max", drawn small after it; null for the value now. After the
    /// value rather than the label, so each item of a pill says its own.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    /// The last minute its line draws, a value a second and oldest first, null where one was missing; null unless it draws
    /// a line.
    /// </summary>
    public IReadOnlyList<GraphPoint?>? Graph { get; init; }

    /// <summary>
    /// How many of its bars' <see cref="OverlayLayout.Segments"/> are lit from the bottom, in its level's color; null unless
    /// it draws bars.
    /// </summary>
    public int? Lit { get; init; }

    /// <summary>Whether the value is marked as the CPU throttling, a dashed line over it, at any level.</summary>
    public bool Throttled { get; init; }
}

/// <summary>A pill: its label and the indexes of its first and last item.</summary>
public readonly record struct Run(string? Label, int First, int Last);

/// <summary>One second of a line: its fill, 0 to 1, and the level the item's lines give it.</summary>
public readonly record struct GraphPoint(double Fill, Level Level);

/// <summary>Turns an overlay's settings and the current readings into what it shows. Pixels are the painter's job.</summary>
public static class OverlayLayout
{
    public const int Segments = 5;

    /// <summary>
    /// What the overlay shows: numbers in <paramref name="culture"/>, the regional format, default labels in the language of
    /// <paramref name="texts"/>, English when there are none, and temperatures in the chosen unit. With a
    /// <paramref name="history"/>, items show their minimum, maximum, average or smoothed value and graphs their last
    /// minute; without one, the value now. An item shown only on alert is left out while its meter is normal, and one of
    /// frames while there are none, unless the window is open, so every item can be selected.
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
        var starts = Runs(overlay.Items, overlay.Style.Layout, texts).Select(run => run.First).ToHashSet();
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
            if (!windowOpen && ((item.AlertOnly && level == Level.Normal) || NoFrames(item, reading)))
            {
                continue;
            }

            // A pill whose first item is left out, shown only on alert, is labeled at its first item shown.
            var labeled = starts.Contains(index) || label != shownLabel;
            var drawn = DrawnOf(overlay, item);
            items.Add(new OverlayItem(labeled ? label : null, value, level, room)
            {
                Id = item.Id,
                Color = item.Color,
                Tag = TagOf(item, culture, texts),
                Graph = drawn == GraphStyle.Line ? GraphOf(item, meter, history) : null,
                Lit = drawn == GraphStyle.Bars ? LitOf(meter) : null,
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

    /// <summary>
    /// What an item shows when it is not its value now, said after its value on the overlay and in the window: its minimum,
    /// maximum or average, or its smoothing; null for the value now. A low of the game's says which first, since its pill
    /// labels it FPS as it does FPS.
    /// </summary>
    public static string? TagOf(ItemConfig item, IFormatProvider culture, Texts? texts = null)
    {
        var said = texts ?? Texts.English;
        var stat = item.Stat switch
        {
            ItemStat.Min => said.Of(OverlayText.MinTag),
            ItemStat.Max => said.Of(OverlayText.MaxTag),
            ItemStat.Average => said.Of(OverlayText.AverageTag),
            ItemStat.Now when item.Smooth > 0 => said.Format(culture, OverlayText.SmoothTag, item.Smooth),
            ItemStat.Now => null,
            _ => null,
        };
        var low = GameSensors.LowOf(item) is { } which ? said.Of(which) : null;
        return low is null ? stat : stat is null ? low : $"{low} {stat}";
    }

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

    /// <summary>The overlay's pills: runs of one label, which an own item breaks with pills, or every item alone with each.</summary>
    public static IReadOnlyList<Run> Runs(OverlayConfig overlay, Texts? texts = null) => Runs(overlay.Items, overlay.Style.Layout, texts);

    /// <summary>The pills of <paramref name="items"/> in <paramref name="layout"/>.</summary>
    public static IReadOnlyList<Run> Runs(IReadOnlyList<ItemConfig> items, PillLayout layout, Texts? texts = null)
    {
        var labels = RunLabels(items, texts);
        var runs = new List<Run>();
        for (var at = 0; at < items.Count; at++)
        {
            if (at == 0 || layout == PillLayout.Each || (layout == PillLayout.Pills && items[at].Own) || labels[at] != labels[at - 1])
            {
                runs.Add(new Run(labels[at], at, at));
            }
            else
            {
                runs[^1] = runs[^1] with { Last = at };
            }
        }

        return runs;
    }

    /// <summary>Whether the overlay has more than one pill, or run in one bar, so that standing it vertical stacks them.</summary>
    public static bool Stacks(OverlayConfig overlay) => Runs(overlay).Count > 1;

    // Frames read only while an app in front presents: the rest of the time there is nothing to show, not a value missing,
    // so the item takes no room, as when the service cannot read frames at all.
    private static bool NoFrames(ItemConfig item, Reading? reading) =>
        Threshold.KindOf(item) is SensorKind.FrameRate or SensorKind.FrameTime && reading?.Usable is null;

    /// <summary>
    /// What an item reads now, its sensors combined and its offset added, or null when it names sensors the snapshot lacks.
    /// A combined item leaves out its sensors without a value, and is missing when none is left.
    /// </summary>
    internal static Reading? ReadingOf(ItemConfig item, IReadOnlyDictionary<string, Reading> readings) =>
        SourceOf(item, readings) is { } reading
            ? reading with { Value = reading.Usable + item.Offset, Bits = item.Bits && reading.Kind == SensorKind.Throughput }
            : null;

    /// <summary>
    /// What an item's lines judge and its graph draws: what it warns by, or its own value, offset included, so its lines
    /// color what it shows; null when its sensor is gone.
    /// </summary>
    internal static Reading? MeterOf(ItemConfig item, IReadOnlyDictionary<string, Reading> readings) =>
        item.WarnBy is not { } other ? ReadingOf(item, readings)
        : other.Sensor is { } sensor ? Named(sensor, other.Auto, readings)
        : readings.GetValueOrDefault(other.Metric ?? "");

    /// <summary>
    /// The level its lines give what it warns by: normal for an item lines cannot judge, without lines or without a value.
    /// An item judged by its own reading is judged as it shows, with its own decimals.
    /// </summary>
    internal static Level LevelOf(ItemConfig item, Reading? meter) =>
        item.Threshold is { } threshold && meter is { } reading && Threshold.Judges(item)
            ? threshold.LevelOf(reading, item.WarnBy is null ? item.Decimals : null)
            : Level.Normal;

    /// <summary>
    /// What an item draws beside its value: its own choice, else its overlay's, whose Auto draws a line for a load or a
    /// game's frames judged by its own reading, whose last minute says more than the value alone, as a stutter's spike,
    /// and bars for the rest; nothing for an item lines cannot judge, which has no level or scale to draw.
    /// </summary>
    public static GraphStyle DrawnOf(OverlayConfig overlay, ItemConfig item) =>
        Threshold.JudgedKind(item) is not { } kind ? GraphStyle.None
        : (item.Graph ?? overlay.Graph) is var chosen and not GraphStyle.Auto ? chosen
        : kind is SensorKind.Load or SensorKind.FrameRate or SensorKind.FrameTime && item.WarnBy is null ? GraphStyle.Line
        : GraphStyle.Bars;

    /// <summary>The pills a single bar holds, each as its item count, one per run; null in the other layouts.</summary>
    public static IReadOnlyList<int>? InnerPills(IReadOnlyList<OverlayItem> items, PillLayout layout) =>
        layout == PillLayout.One ? [.. Groups(items).Select(group => group.Count)] : null;

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

    /// <summary>
    /// Loads, duties and temperatures all read on a 0 to 100 scale, a fan's speed on the range of its lines; a frame rate
    /// on 0 to 240, the fastest common screen, and a frame time on 0 to 100 ms, where it turns critical by default.
    /// </summary>
    internal static double FillOf(double value, SensorKind kind = SensorKind.Load) =>
        Math.Clamp(value / (kind == SensorKind.FrameRate ? 240 : kind == SensorKind.FrameTime ? 100 : Threshold.MaxOf(kind)), 0, 1);

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

    // Without a history, a graph has only the value now.
    private static IReadOnlyList<GraphPoint?> GraphOf(ItemConfig item, Reading? meter, ItemHistory? history) =>
        history is not null ? history.Graph(item)
        : [meter is { Usable: { } value } read ? new GraphPoint(FillOf(value, read.Kind), LevelOf(item, meter)) : null];

    // Any value above zero lights a segment, so bars that show none read as zero or nothing read.
    private static int LitOf(Reading? meter)
    {
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
