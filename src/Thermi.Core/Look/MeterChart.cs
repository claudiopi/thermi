using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Look;

/// <summary>One of a meter's two lines.</summary>
public enum ChartLine
{
    Warning,
    Critical,
}

/// <summary>What the chart says about its lines and the box a knob opens, in the language of the translations.</summary>
[Translated]
public enum ChartText
{
    /// <summary>A typed value off the track: its ends in the blanks, the second with the meter's unit.</summary>
    OutOfRange,

    /// <summary>Typed text that is no number: an example value in the blank.</summary>
    NotANumber,

    /// <summary>The warning pushed below the critical that moved past it: where it went, with its unit, in the blank.</summary>
    WarningPushed,

    /// <summary>The critical pushed above the warning that moved past it: where it went, with its unit, in the blank.</summary>
    CriticalPushed,

    /// <summary>Under the chart: its lowest, average and highest reading, the last with the unit.</summary>
    Stats,

    /// <summary>What Narrator says of the chart: its lowest and highest reading and the last, each with the unit.</summary>
    Summary,

    /// <summary>Above a duty's chart, which reads in % where its item reads in RPM.</summary>
    DutyTitle,

    /// <summary>Above the chart of the RAM in use, which reads in % where its item reads in GB.</summary>
    RamTitle,

    /// <summary>The button of lines that fire most of the time, which puts back the defaults: the warning, then the critical with its unit.</summary>
    UseLines,

    /// <summary>The same button when the default is a warning alone, with its unit.</summary>
    UseLine,

    /// <summary>The same button when the default is no lines.</summary>
    UseNoLines,

    /// <summary>Under the chart's left end: how many minutes ago its first reading came.</summary>
    MinutesAgo,

    /// <summary>The same, under a minute: how many seconds ago.</summary>
    SecondsAgo,

    /// <summary>How long ago a point of the chart came, past a minute and not on one: its minutes and seconds.</summary>
    MinutesSecondsAgo,

    /// <summary>A point of the chart: its second's reading, or <see cref="NoReading"/>, and how long ago it came.</summary>
    Point,

    NoReading,
}

/// <summary>
/// A meter in its own unit: its type, its default lines (either may be off), the knobs' step, and how far a line moved
/// past the other pushes it. Lines run from 0 to <see cref="Max"/>.
/// </summary>
public sealed record MeterScale(SensorKind Kind, double? Warning, double? Critical, double Step, double Push)
{
    public double Max => Threshold.MaxOf(Kind);

    /// <summary>Whether its lines warn below a value, the critical under the warning, as a frame rate's.</summary>
    public bool Low => Threshold.WarnsLow(Kind);
}

/// <summary>The lines after one moved, and the other when it was pushed out of the way, else null.</summary>
public sealed record MovedLines(double? Warning, double? Critical, ChartLine? Pushed);

/// <summary>The chart of a meter: its scale per type, its range over the readings and lines, and its color at each value.</summary>
public static class MeterChart
{
    // The room a temperature's chart keeps under its lowest value and over its highest, and what its ends round to.
    private const double BelowTemperature = 6;
    private const double AboveTemperature = 3;

    // A speed's chart keeps this much over its highest value, and a fan's never ends under its smallest top.
    private const double Headroom = 1.15;
    private const double FanChartStep = 500;

    private const double CriticalAbove = 1.3;

    // A throughput's chart ends at 1 KB/s at least, so an idle line lies flat at the bottom instead of filling it with noise.
    private const double SmallestThroughputTop = 1000;

    /// <summary>
    /// The scale of a sensor or metric as a meter, or null when it cannot be one. Lines move by one degree or percent and
    /// push by 5; a fan's by 50 RPM and push by 100; a throughput's by 100 KB/s and push by 1 MB/s.
    /// </summary>
    public static MeterScale? Of(string meter, Snapshot snapshot)
    {
        var metric = Metrics.Find(meter);
        var kind = metric?.Kind ?? (SensorMap.Find(snapshot, meter) is { } sensor ? SensorMap.KindOf(sensor) : null);
        if (kind is not { } known || !Threshold.CanMeter(known))
        {
            return null;
        }

        var lines = Threshold.Default(metric is null ? new WarnByConfig { Sensor = meter } : new WarnByConfig { Metric = meter }, snapshot);
        var (step, push) = known == SensorKind.Fan ? (50.0, 100.0)
            : known == SensorKind.Throughput ? (100e3, 1e6)
            : (1.0, 5.0);
        return new MeterScale(known, lines?.Warning, lines?.Critical, step, push);
    }

    /// <summary>
    /// The scale of the chart of an item's own reading, for an item lines cannot judge: no lines, and null for a reading
    /// that is no number to draw, as a time of day, an address or a time since start.
    /// </summary>
    public static MeterScale? OfReading(string sensor, Snapshot snapshot)
    {
        var kind = Metrics.Find(sensor)?.Kind ?? (SensorMap.Find(snapshot, sensor) is { } read ? SensorMap.KindOf(read) : null);
        return kind is { } known and not (SensorKind.Time or SensorKind.Address or SensorKind.Duration) ? new MeterScale(known, null, null, 1, 5) : null;
    }

    /// <summary>
    /// The chart's range over <paramref name="readings"/> and the lines. A percentage's runs from 0 to 100; a temperature's
    /// from the lowest less 6, down to tens and never under 0, to the highest plus 3, up to fives and never over 100 °C; a
    /// fan's from 0 to the highest, at least 500, plus 15 %, up to 500 RPM; a throughput's, and any other reading's, from 0
    /// to the highest plus 15 %, up to a 1-2-5 step.
    /// </summary>
    public static (double From, double To) Range(MeterScale scale, IReadOnlyList<double> readings)
    {
        var values = readings.Where(double.IsFinite).Concat(new[] { scale.Warning, scale.Critical }.OfType<double>()).ToList();
        var highest = values.Count == 0 ? 0 : values.Max();
        if (scale.Kind == SensorKind.Temperature && values.Count > 0)
        {
            return (Math.Max(0, Math.Floor((values.Min() - BelowTemperature) / 10) * 10), Math.Min(100, Math.Ceiling((highest + AboveTemperature) / 5) * 5));
        }

        return scale.Kind == SensorKind.Fan ? (0, Math.Ceiling(Math.Max(highest, FanChartStep) * Headroom / FanChartStep) * FanChartStep)
            : scale.Kind == SensorKind.Throughput ? (0, OneTwoFive(Math.Max(highest * Headroom, SmallestThroughputTop)))
            : scale.Kind is SensorKind.Load or SensorKind.Control ? (0, 100)
            : (0, OneTwoFive(Math.Max(highest * Headroom, 1)));
    }

    /// <summary>
    /// Where the chart's color changes over its range, bottom up, as the overlay's meters color their levels: normal below
    /// the lines or without them, warning from the warning and critical from the critical; for a <paramref name="low"/>
    /// meter, critical up to the critical, warning up to the warning and normal above. Each stop's level holds up to the
    /// next, so the line and its fill change color where they cross a line.
    /// </summary>
    public static IReadOnlyList<(double At, Level Level)> Stops(double from, double to, double? warning, double? critical, bool low = false)
    {
        var stops = new List<(double At, Level Level)> { (from, low ? LevelAbove(from, warning, critical) : LevelAt(from, warning, critical)) };
        foreach (var line in new[] { warning, critical }.OfType<double>().Where(line => line > from && line <= to).Order())
        {
            if ((low ? LevelAbove(line, warning, critical) : LevelAt(line, warning, critical)) is var level && level != stops[^1].Level)
            {
                stops.Add((line, level));
            }
        }

        return stops;
    }

    /// <summary>
    /// The chart's span, a reading a second: from its first reading on, so minutes the overlay and the window had not yet
    /// read leave no empty stretch, while a gap after it stays one; and how long ago its first reading came, the last being
    /// now, as a lone reading is, "40 s ago" or, past a minute, rounded to minutes, as "4 min ago".
    /// </summary>
    public static (IReadOnlyList<double?> Readings, string Start) Span(IReadOnlyList<double?> history, ValueFormat format)
    {
        var first = history.Count - history.SkipWhile(value => value is null).Count();
        var seconds = Math.Max(0, history.Count - first - 1);
        return ([.. history.Skip(first)], Ago(seconds < 60 ? seconds : (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero) * 60, format));
    }

    /// <summary>
    /// Each second of the span as its point says it: its reading as <paramref name="said"/> words it, or none in a gap, and
    /// how long ago it came, the last now.
    /// </summary>
    public static IReadOnlyList<string> Points(IReadOnlyList<double?> readings, Func<double, string> said, ValueFormat format) =>
    [
        .. readings.Select((reading, index) => format.Texts.Format(
            format.Numbers,
            ChartText.Point,
            reading is { } value ? said(value) : format.Texts.Of(ChartText.NoReading),
            Ago(readings.Count - 1 - index, format))),
    ];

    // The axis rounds its start to minutes past the first; a point says its own second.
    private static string Ago(int seconds, ValueFormat format) => seconds switch
    {
        0 => format.Texts.Of(UiText.ChartNow),
        < 60 => format.Texts.Format(format.Numbers, ChartText.SecondsAgo, seconds),
        _ when seconds % 60 == 0 => format.Texts.Format(format.Numbers, ChartText.MinutesAgo, seconds / 60),
        _ => format.Texts.Format(format.Numbers, ChartText.MinutesSecondsAgo, seconds / 60, seconds % 60),
    };

    /// <summary>The lowest, average and highest of the readings, a missing one or a gap left out; null when there are none.</summary>
    public static (double Min, double Average, double Max)? Summary(IEnumerable<double?> readings)
    {
        var values = readings.OfType<double>().Where(double.IsFinite).ToList();
        return values.Count == 0 ? null : (values.Min(), values.Average(), values.Max());
    }

    /// <summary>
    /// Whether the lowest line was reached for more than half of the last ten minutes, a second each: a line nobody would
    /// set on purpose, as one carried over from an older thermi.
    /// </summary>
    public static bool FiresMostly(IEnumerable<double?> readings, double? warning, double? critical, bool low = false) =>
        (warning ?? critical) is { } line && readings.Count(value => low ? value <= line : value >= line) * 2 > SensorHistory.Kept.TotalSeconds;

    /// <summary>
    /// The lines once <paramref name="moved"/> is where it was put: when it went past the other, the other moves
    /// <see cref="MeterScale.Push"/> beyond it, held on the track, so the warning stays below the critical, or above it on a
    /// low meter.
    /// </summary>
    public static MovedLines Pushed(MeterScale scale, double? warning, double? critical, ChartLine moved)
    {
        if (warning is not { } first || critical is not { } second || new Threshold(first, second).InOrderFor(scale.Kind))
        {
            return new MovedLines(warning, critical, null);
        }

        var push = scale.Low ? -scale.Push : scale.Push;
        return moved == ChartLine.Warning
            ? new MovedLines(first, Math.Clamp(first + push, 0, scale.Max), ChartLine.Critical)
            : new MovedLines(Math.Clamp(second - push, 0, scale.Max), second, ChartLine.Warning);
    }

    /// <summary>
    /// Where a line added to the chart comes: at its default, else <see cref="MeterScale.Push"/> beyond the other line, else
    /// near the top of the track. A fan's or a throughput's track ends far past any reading, so there it goes over the
    /// highest of <paramref name="readings"/>: a warning 15 % over, a critical 30 %, rounded up to the knobs' step.
    /// </summary>
    public static double Added(MeterScale scale, Threshold? lines, ChartLine line, IReadOnlyList<double> readings)
    {
        var warning = line == ChartLine.Warning;
        if ((warning ? scale.Warning : scale.Critical) is { } made)
        {
            return made;
        }

        // A low meter, a frame rate's, always has its default lines, so it never gets this far.
        if ((warning ? lines?.Critical : lines?.Warning) is { } other)
        {
            return Math.Clamp(warning ? other - scale.Push : other + scale.Push, 0, scale.Max);
        }

        if (scale.Kind is not (SensorKind.Fan or SensorKind.Throughput))
        {
            return scale.Max * (warning ? 0.8 : 0.9);
        }

        var least = scale.Kind == SensorKind.Fan ? FanChartStep : SmallestThroughputTop;
        var highest = Math.Max(least, readings.Where(double.IsFinite).DefaultIfEmpty(0).Max());
        // A product a hair over a whole step, as 1,087 × 1.15, is that step.
        var steps = Math.Ceiling(Math.Round(highest * (warning ? Headroom : CriticalAbove) / scale.Step, 6));
        return Math.Min(steps * scale.Step, scale.Max);
    }

    /// <summary>
    /// What the box a knob opens says of the text typed in it, in the shown unit, or null for a value it keeps or for an
    /// empty box, which turns the line off: a value off the track names the track's ends, the second with the meter's
    /// unit, and text that is no number gives the line's default as an example.
    /// </summary>
    public static string? Check(MeterScale scale, string typed, ValueFormat format)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        if (!double.TryParse(typed, NumberStyles.Float, format.Numbers, out var shown))
        {
            return format.Texts.Format(
                format.Numbers,
                ChartText.NotANumber,
                ReadingFormatter.Format(scale.Kind, scale.Critical ?? scale.Warning ?? scale.Max, format, units: false));
        }

        var kept = scale.Kind == SensorKind.Temperature ? Temperatures.Kept(shown, format.Temperature) : shown;
        return kept >= 0 && kept <= scale.Max ? null
            : format.Texts.Format(
                format.Numbers,
                ChartText.OutOfRange,
                ReadingFormatter.Format(scale.Kind, 0, format, units: false),
                ReadingFormatter.Format(scale.Kind, scale.Max, format));
    }

    /// <summary>What the chart says when the line moved pushed the other out of its way, or null when it did not.</summary>
    public static string? PushText(MeterScale scale, MovedLines lines, ValueFormat format) => lines switch
    {
        { Pushed: ChartLine.Warning, Warning: { } warning } =>
            format.Texts.Format(format.Numbers, ChartText.WarningPushed, ReadingFormatter.Format(scale.Kind, warning, format)),
        { Pushed: ChartLine.Critical, Critical: { } critical } =>
            format.Texts.Format(format.Numbers, ChartText.CriticalPushed, ReadingFormatter.Format(scale.Kind, critical, format)),
        _ => null,
    };

    private static Level LevelAt(double value, double? warning, double? critical) =>
        value >= critical ? Level.Critical
        : value >= warning ? Level.Warning
        : Level.Normal;

    // A low meter's level just above a value: a line there belongs to the level below it.
    private static Level LevelAbove(double value, double? warning, double? critical) =>
        value < critical ? Level.Critical
        : value < warning ? Level.Warning
        : Level.Normal;

    private static double OneTwoFive(double value)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var multiple = new[] { 1.0, 2, 5, 10 }.First(each => each * power >= value * (1 - 1e-12));
        return multiple * power;
    }
}
