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
/// <param name="chart">The overlay's ten minutes, and the window's own history of its items.</param>
/// <param name="sensors">The readings now and whether the service runs.</param>
/// <param name="readings">What the window has read since it opened.</param>
internal sealed class WindowCharts(WindowEditor editor, ChartHistory chart, ISensorFeed sensors, Readings readings)
{
    private readonly Dictionary<string, string> _pushed = new(StringComparer.Ordinal);

    /// <summary>A fresh snapshot, kept for each item's chart and value, by item, so both go with it when overlays are deleted, merged or moved.</summary>
    public void Arrived()
    {
        if (sensors.Latest is { } snapshot)
        {
            chart.Items.Add([.. editor.Config.Overlays.Select(overlay => (overlay, readings.ReadOf(snapshot, overlay)))]);
        }
    }

    /// <summary>The window's history of its items, the overlay's taken in, for their minimum, maximum, average and smoothing.</summary>
    public ItemHistory Own => chart.Items;

    public IReadOnlyList<double?> History(string id) => chart.Of(id, chart.Items.TenMinutes(id));

    /// <summary>An item's line as the overlay draws it: the last minute of its chart, so it shows whole from the window's opening.</summary>
    public IReadOnlyList<GraphPoint?> Graph(ItemConfig item) => ItemHistory.GraphOf(item, History(item.Id));

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
        var (span, start) = MeterChart.Span(history, format);
        var values = history.OfType<double>().ToList();
        var (warning, critical) = Threshold.Judges(item) ? (item.Threshold?.Warning, item.Threshold?.Critical) : default;
        var (from, to) = MeterChart.Range(scale with { Warning = warning, Critical = critical }, values);
        var summary = MeterChart.Summary(history);
        var defaults = Threshold.Default(item, readings.Known(sensors.Latest));
        string Said(double value, bool units = true)
        {
            return ReadingFormatter.Format(scale.Kind, value, format, units);
        }

        return new ChartView(
            scale.Max,
            scale.Step,
            from,
            to,
            span,
            warning,
            critical,
            warning is { } low ? Said(low) : null,
            critical is { } high ? Said(high) : null,
            [.. MeterChart.Stops(from, to, warning, critical).Select(stop => new ColorStop(stop.At, stop.Level))],
            summary is (var min, var average, var max)
                ? format.Texts.Format(format.Numbers, ChartText.Stats, Said(min, units: false), Said(average, units: false), Said(max))
                : null,
            summary is (var least, _, var most) ? format.Texts.Format(format.Numbers, ChartText.Summary, Said(least), Said(most), Said(values[^1])) : "",
            EmptyOf(meter, values.Count),
            _pushed.GetValueOrDefault(id),
            TitleOf(item, meter, scale, format.Texts),
            Said(from),
            Said(to),
            MeterChart.FiresMostly(history, warning, critical) && item.Threshold != defaults ? DefaultsText(defaults, scale.Kind, format) : null,
            start);
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
    private static string? TitleOf(ItemConfig item, WarnByConfig meter, MeterScale scale, Texts texts) =>
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
        if (ItemOf(editor.Config, id) is not { } item || !Threshold.Judges(item) || ScaleOf(item.WarnBy ?? Threshold.Own(item)) is not { } scale)
        {
            return Texts.Key(Refusal.NothingToJudge);
        }

        if (edit is TypedLineEdit { Typed: var text } && MeterChart.Check(scale, text, format) is { } said)
        {
            return said;
        }

        var value = edit switch
        {
            LineEdit moved => moved.Value,
            TypedLineEdit typed => Typed(scale, typed.Typed, format),
            _ => MeterChart.Added(scale, item.Threshold, line, [.. History(id).OfType<double>()]),
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
    private MeterScale? ScaleOf(WarnByConfig meter) => MeterChart.Of(meter.Sensor ?? meter.Metric ?? meter.Auto ?? "", readings.Known(sensors.Latest));

    // What the chart draws: what the item warns by, or for one lines cannot judge its own reading, which has no lines.
    private (WarnByConfig Meter, MeterScale Scale)? Charted(ItemConfig item)
    {
        var meter = item.WarnBy ?? Threshold.Own(item);
        if (Threshold.Judges(item))
        {
            return ScaleOf(meter) is { } scale ? (meter, scale) : null;
        }

        return MeterChart.OfReading(meter.Sensor ?? meter.Metric ?? meter.Auto ?? "", readings.Known(sensors.Latest)) is { } own ? (meter, own) : null;
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

    private ChartEmpty? EmptyOf(WarnByConfig meter, int values) =>
        sensors.State == ServiceState.Stopped ? ChartEmpty.Stopped
        : values > 0 ? null
        : OnCpu(meter) && SensorMap.DriverMayBeMissing(readings.Known(sensors.Latest)) ? ChartEmpty.Driver
        : ChartEmpty.None;

    private bool OnCpu(WarnByConfig meter) =>
        Metrics.Find(meter.Metric ?? meter.Auto) is { } metric ? Metrics.DeviceOf(metric) == DeviceKind.Cpu
        : meter.Sensor is { } sensor && SensorMap.Find(readings.Known(sensors.Latest), sensor) is { } read && SensorMap.DeviceOf(read) == DeviceKind.Cpu;
}
