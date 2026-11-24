using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Ui.Editor;

namespace Thermi.Ui.Views;

/// <summary>
/// Each item's chart: the meter's ten minutes, the overlay's under the window's own, its lines and their texts, the colors
/// along it and its summary, all from Core's <see cref="MeterChart"/>; and the edits of its lines, a line moved past the
/// other pushing it, which the chart then says. Lines are kept in the meter's own unit, °C for a temperature, and typed in
/// the unit shown. Used on the window's thread alone.
/// </summary>
/// <param name="editor">Whose items are charted, and where a line's edit goes.</param>
/// <param name="chart">The overlay's ten minutes.</param>
/// <param name="sensors">The readings now and whether the service runs.</param>
/// <param name="readings">What the window has read since it opened.</param>
/// <param name="time">The clock of the window's own ten minutes.</param>
internal sealed class WindowCharts(WindowEditor editor, ChartHistory chart, ISensorFeed sensors, Readings readings, TimeProvider time)
{
    // The color stops are found by walking the chart; this many steps at most, so a wide one is walked quickly.
    private const int Walk = 1000;

    // By item, so an item's chart goes with it when overlays are deleted, merged or moved.
    private readonly ItemHistory _own = new(time);
    private readonly Dictionary<string, string> _pushed = new(StringComparer.Ordinal);

    /// <summary>A fresh snapshot, kept for each item's chart.</summary>
    public void Arrived()
    {
        if (sensors.Latest is { } snapshot)
        {
            _own.Add([.. editor.Config.Overlays.Select(overlay => (overlay, SensorMap.Read(snapshot, overlay)))]);
        }
    }

    public IReadOnlyList<double?> History(string id) => chart.Of(id, _own.TenMinutes(id));

    /// <summary>
    /// The item's chart: its meter's, or without a meter its own reading's with no lines; null for a reading that is no
    /// number to draw, or a meter of a type that has no chart.
    /// </summary>
    public ChartView? Chart(string id, ValueFormat format)
    {
        if (ItemOf(editor.Config, id) is not { } item || Charted(item) is not var (meter, scale))
        {
            return null;
        }

        var history = History(id);
        var values = history.OfType<double>().ToList();
        var (warning, critical) = item.Meter is null ? default : (item.Threshold?.Warning, item.Threshold?.Critical);
        var (from, to) = MeterChart.Range(scale with { Warning = warning, Critical = critical }, values);
        var summary = MeterChart.Summary(history);
        var defaults = item.Meter is null ? null : Threshold.Default(meter, readings.Known(sensors.Latest));
        string Said(double value, bool units = true)
        {
            return ReadingFormatter.Format(scale.Kind, value, format, units);
        }

        return new ChartView(
            scale.Max,
            scale.Step,
            from,
            to,
            history,
            warning,
            critical,
            warning is { } low ? Said(low) : null,
            critical is { } high ? Said(high) : null,
            Colors(scale, from, to, warning, critical),
            summary is (var min, var average, var max)
                ? format.Texts.Format(format.Numbers, ChartText.Stats, Said(min, units: false), Said(average, units: false), Said(max))
                : null,
            summary is (var least, _, var most) ? format.Texts.Format(format.Numbers, ChartText.Summary, Said(least), Said(most), Said(values[^1])) : "",
            EmptyOf(meter, values.Count),
            _pushed.GetValueOrDefault(id),
            TitleOf(item, meter, scale, format.Texts),
            Said(from),
            Said(to),
            MeterChart.FiresMostly(history, warning, critical) && item.Threshold != defaults ? DefaultsText(defaults, scale.Kind, format) : null);
    }

    // The button of lines that fire most of the time says the defaults it puts back.
    private static string DefaultsText(Threshold? defaults, SensorKind kind, ValueFormat format) =>
        (defaults?.Warning, defaults?.Critical) switch
        {
            ({ } warning, { } critical) => format.Texts.Format(
                format.Numbers,
                ChartText.UseLines,
                ReadingFormatter.Format(kind, warning, format, units: false),
                ReadingFormatter.Format(kind, critical, format)),
            (null, null) => format.Texts.Of(ChartText.UseNoLines),
            var (warning, critical) =>
                format.Texts.Format(format.Numbers, ChartText.UseLine, ReadingFormatter.Format(kind, (warning ?? critical)!.Value, format)),
        };

    // A chart in another unit than its item's value says what it reads: a fan's duty, or the RAM in use as its load.
    private static string? TitleOf(ItemConfig item, MeterConfig meter, MeterScale scale, Texts texts) =>
        scale.Kind == SensorKind.Control ? texts.Of(ChartText.DutyTitle)
        : (meter.Metric ?? meter.Auto) == Metrics.RamLoad.Id && (item.Metric ?? item.Auto) == Metrics.RamUsed.Id ? texts.Of(ChartText.RamTitle)
        : null;

    /// <summary>
    /// Moves, types or adds one of the item's lines as one step, the other pushed out of its way: null when made, else the
    /// refusal's key, or for a typed value what the box says of it.
    /// </summary>
    public string? Line(PageEdit edit, ValueFormat format)
    {
        var (id, line) = edit switch
        {
            LineEdit moved => (moved.Item, moved.Line),
            TypedLineEdit typed => (typed.Item, typed.Line),
            AddLineEdit added => (added.Item, added.Line),
            _ => throw new ArgumentException("Not a line's edit.", nameof(edit)),
        };
        if (ItemOf(editor.Config, id) is not { Meter: { } meter } item || ScaleOf(meter) is not { } scale)
        {
            return Texts.Key(Refusal.NoMeter);
        }

        if (edit is TypedLineEdit { Typed: var text } && MeterChart.Check(scale, text, format) is { } said)
        {
            return said;
        }

        var value = edit switch
        {
            LineEdit moved => moved.Value,
            TypedLineEdit typed => Typed(scale, typed.Typed, format),
            _ => Added(scale, item.Threshold, line),
        };
        var lines = MeterChart.Pushed(
            scale,
            line == ChartLine.Warning ? value : item.Threshold?.Warning,
            line == ChartLine.Critical ? value : item.Threshold?.Critical,
            line);
        var result = editor.Edit("line", config => ItemEdits.SetThreshold(config, id, lines.Warning, lines.Critical));
        if (result.Refused is { } refused)
        {
            return Texts.Key(refused);
        }

        _pushed.Remove(id);
        if (MeterChart.PushText(scale, lines, format) is { } pushed)
        {
            _pushed[id] = pushed;
        }

        return null;
    }

    private static ItemConfig? ItemOf(ThermiConfig config, string id) =>
        config.Overlays.SelectMany(overlay => overlay.Items).FirstOrDefault(item => item.Id == id);

    // A sensor the service no longer reads is known by the last snapshot the window had.
    private MeterScale? ScaleOf(MeterConfig meter) => MeterChart.Of(meter.Sensor ?? meter.Metric ?? meter.Auto ?? "", readings.Known(sensors.Latest));

    // What the chart draws: the meter, or the item's own reading, which has no lines.
    private (MeterConfig Meter, MeterScale Scale)? Charted(ItemConfig item)
    {
        if (item.Meter is { } meter)
        {
            return ScaleOf(meter) is { } scale ? (meter, scale) : null;
        }

        var itself = new MeterConfig { Metric = item.Metric, Sensor = item.Sensor ?? (item.Sensors is [var first, ..] ? first : null), Auto = item.Auto };
        return MeterChart.OfReading(itself.Sensor ?? itself.Metric ?? itself.Auto ?? "", readings.Known(sensors.Latest)) is { } own ? (itself, own) : null;
    }

    // Empty text turns the line off; a number is typed in the unit shown, and kept in the meter's own.
    private static double? Typed(MeterScale scale, string typed, ValueFormat format)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var shown = double.Parse(typed, NumberStyles.Float, format.Numbers);
        return scale.Kind == SensorKind.Temperature ? Temperatures.Kept(shown, format.Temperature) : shown;
    }

    // A line added comes back at its default, or without one a push beyond the other, or near the top of an empty track.
    private static double Added(MeterScale scale, Threshold? lines, ChartLine line) => line == ChartLine.Warning
        ? scale.Warning ?? (lines?.Critical is { } critical ? Math.Max(critical - scale.Push, 0) : scale.Max * 0.8)
        : scale.Critical ?? (lines?.Warning is { } warning ? Math.Min(warning + scale.Push, scale.Max) : scale.Max * 0.9);

    // Where the color changes along the chart, by Core's rule; with no line, the window blends from cool to the middle color.
    private static List<ColorStop> Colors(MeterScale scale, double from, double to, double? warning, double? critical)
    {
        var stops = new List<ColorStop> { new(from, MeterChart.ColorAt(from, warning, critical)) };
        var step = Math.Max(scale.Step, (to - from) / Walk);
        for (var at = from + step; at <= to; at += step)
        {
            if (MeterChart.ColorAt(at, warning, critical) is var color && color != stops[^1].Color)
            {
                stops.Add(new ColorStop(at, color));
            }
        }

        return warning is null && critical is null ? [.. stops, new ColorStop(to, ChartColor.Middle)] : stops;
    }

    private ChartEmpty? EmptyOf(MeterConfig meter, int values) =>
        sensors.State == ServiceState.Stopped ? ChartEmpty.Stopped
        : values > 0 ? null
        : OnCpu(meter) && SensorMap.DriverMayBeMissing(readings.Known(sensors.Latest)) ? ChartEmpty.Driver
        : ChartEmpty.None;

    private bool OnCpu(MeterConfig meter) =>
        Metrics.Find(meter.Metric ?? meter.Auto) is { } metric ? Metrics.DeviceOf(metric) == DeviceKind.Cpu
        : meter.Sensor is { } sensor && SensorMap.Find(readings.Known(sensors.Latest), sensor) is { } read && SensorMap.DeviceOf(read) == DeviceKind.Cpu;
}
