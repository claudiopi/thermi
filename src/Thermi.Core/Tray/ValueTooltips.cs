using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>What pointing at a value says: the item's name, value and level, its minimum and maximum, and how to edit it; or why it is missing and what to do.</summary>
public static class ValueTooltips
{
    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    /// <summary>The tooltip's lines, one per line break, of a tooltip <see cref="ThermiMark.Overlay"/> gave for the overlay.</summary>
    public static string Text(ThermiConfig config, int overlay, TrayInput input, ValueTooltip tooltip)
    {
        var texts = input.Texts;
        var shown = config.Overlays[overlay];
        var item = shown.Items.FirstOrDefault(each => each.Id == tooltip.ItemId);
        if (item is null || tooltip.Hint == ValueHint.SensorsStopped)
        {
            return texts.Of(OverlayText.SensorsStoppedHint);
        }

        var name = NameOf(item, texts);
        if (tooltip.Hint is ValueHint.NeedsDriver or ValueHint.ReadsNothing)
        {
            return texts.Format(input.Numbers, tooltip.Hint == ValueHint.NeedsDriver ? OverlayText.NeedsDriver : OverlayText.ReadsNothing, name);
        }

        var snapshot = ThermiMark.Live(input);
        var readings = snapshot is null ? NoReadings : SensorMap.Read(snapshot, shown);
        var now = OverlayLayout.ReadingOf(item, readings);
        var history = input.History;
        string Value(ItemStat stat)
        {
            return Formatted(history is null ? now : history.Shown(item with { Stat = stat }, now), item, config, input);
        }

        var value = texts.Format(input.Numbers, OverlayText.NameAndValue, name, Value(item.Stat));
        var first = tooltip.Level == Level.Normal ? value
            : texts.Format(input.Numbers, TrayText.AtLevel, value, texts.Of(tooltip.Level == Level.Critical ? TrayText.Critical : TrayText.Warning));
        var minMax = texts.Format(input.Numbers, OverlayText.MinAndMax, Value(ItemStat.Min), Value(ItemStat.Max));
        var lines = snapshot is null ? [first, minMax] : About(item, (shown, snapshot), (first, minMax), tooltip.Level, config, input);
        lines.Add(texts.Of(tooltip.Hint == ValueHint.SetUp ? OverlayText.SetUp : OverlayText.DoubleClickToEdit));
        return string.Join('\n', lines);
    }

    // The value, the model of the CPU or GPU it is read from, its minimum and maximum, and why it may be high.
    private static List<string> About(ItemConfig item, (OverlayConfig Overlay, Snapshot Snapshot) read, (string Value, string MinMax) lines, Level level, ThermiConfig config, TrayInput input)
    {
        var sensor = SensorOf(item, read.Overlay, read.Snapshot);
        var device = sensor is null ? null : SensorMap.DeviceOf(sensor);
        List<string> about = [lines.Value];
        if (device is DeviceKind.Cpu or DeviceKind.Gpu)
        {
            about.Add(sensor!.Hardware);
        }

        about.Add(lines.MinMax);
        about.AddRange(Why(item, read, device, level, config, input));
        return about;
    }

    /// <summary>Whether a value of the CPU or the GPU is critical on any overlay, while which its tooltip names what is busiest, so only then is it read.</summary>
    public static bool WantsBusiest(ThermiConfig config, Snapshot snapshot) =>
        config.Overlays.Any(overlay =>
        {
            var readings = SensorMap.Read(snapshot, overlay);
            return overlay.Items.Any(item => OverlayLayout.LevelOf(item, OverlayLayout.MeterOf(item, readings)) == Level.Critical
                && SensorOf(item, overlay, snapshot) is { } sensor && SensorMap.DeviceOf(sensor) is DeviceKind.Cpu or DeviceKind.Gpu);
        });

    // The sensor an item shows: its own, the first it combines, or the one its metric picks on this overlay.
    private static RawSensor? SensorOf(ItemConfig item, OverlayConfig overlay, Snapshot snapshot) =>
        (item.Sensor ?? (item.Sensors is [var first, ..] ? first : null)) is { } id ? SensorMap.Find(snapshot, id)
        : Metrics.Find(item.Metric) is { } metric ? SensorMap.Chosen(snapshot, overlay).GetValueOrDefault(metric.Id)
        : null;

    // Why a CPU value may be high: the CPU throttling, and at critical what is busiest on its device now.
    private static List<string> Why(ItemConfig item, (OverlayConfig Overlay, Snapshot Snapshot) read, DeviceKind? device, Level level, ThermiConfig config, TrayInput input)
    {
        var (texts, numbers) = (input.Texts, input.Numbers);
        string Shown(SensorKind kind, double value)
        {
            return ReadingFormatter.Format(new Reading(kind, value), numbers, units: true, config.Temperature, texts: texts);
        }

        var why = new List<string>();
        if (Throttling.Shows(item, read.Snapshot) && Throttling.Of(read.Snapshot, read.Overlay, input.BaseClock) is { } throttled)
        {
            why.Add(texts.Format(numbers, OverlayText.Throttling, Shown(SensorKind.Clock, throttled.Clock), Shown(SensorKind.Clock, throttled.BaseClock), Shown(SensorKind.Temperature, throttled.Temperature)));
        }

        var busy = level != Level.Critical ? null : device == DeviceKind.Cpu ? input.Busiest?.Cpu : device == DeviceKind.Gpu ? input.Busiest?.Gpu : null;
        if (busy is not null)
        {
            why.Add(texts.Format(numbers, device == DeviceKind.Cpu ? OverlayText.BusiestCpu : OverlayText.BusiestGpu, busy.Name, Shown(SensorKind.Load, busy.Share)));
        }

        return why;
    }

    // A metric's item under its default name is named in the language shown; any other by its own name.
    private static string NameOf(ItemConfig item, Texts texts) =>
        Metrics.Find(item.Metric) is { } metric && (item.Name is null || item.Name == Texts.English.Of(metric.Name)) ? texts.Of(metric.Name)
        : item.Name ?? OverlayLayout.LabelOf(item, texts) ?? item.Id;

    private static string Formatted(Reading? reading, ItemConfig item, ThermiConfig config, TrayInput input) =>
        reading is { } read ? ReadingFormatter.Format(read, input.Numbers, units: true, config.Temperature, item.Decimals, input.Texts) : ReadingFormatter.Missing;
}
