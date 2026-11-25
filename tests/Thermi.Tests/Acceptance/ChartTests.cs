using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// A meter's lines and chart follow its type, in the unit of what it reads.
/// </summary>
public sealed class ChartTests
{
    private const string IntelPackage = "/intelcpu/0/temperature/0";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly ValueFormat InEnglish = new(Texts.English, English);

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    // An Intel CPU whose package reads 60 °C, 45 under its TjMax of 105, beside the recorded adapters and drive and a
    // made-up battery.
    private static Snapshot WithIntel => new([
        .. Desktop.Sensors,
        .. MadeUpHardware.Desktop().Sensors.Where(sensor => sensor.HardwareType is "Network" or "Storage"),
        .. MadeUpHardware.Laptop().Sensors.Where(sensor => sensor.HardwareType == "Battery"),
        new RawSensor(IntelPackage, "Cpu", "Intel Core Ultra 9 285K", "Temperature", "CPU Package", 60),
        new RawSensor("/intelcpu/0/temperature/1", "Cpu", "Intel Core Ultra 9 285K", "Temperature", "CPU Core #1 Distance to TjMax", 45),
    ]);

    // A meter's sensor, read once at its value in the recording, and what it gives: its type or null when it cannot be a
    // meter, its default lines, its step and push, the chart's range, then the track's ends that the box names for a value
    // off it. A line moved past the other pushes it by the push, and the chart says where it went. After #, the meters an item
    // of that sensor is offered, = for itself: only readings of the item, a fan's duty beside its speed and the other way
    // round, never another device's or another kind's.
    [Theory]
    [InlineData("/amdcpu/0/temperature/2", SensorKind.Temperature, 90.0, 95.0, 1.0, 5.0, "70 100|0|100 °C#=#90 95")]
    [InlineData(IntelPackage, SensorKind.Temperature, 95.0, 100.0, 1.0, 5.0, "50 100|0|100 °C#=#95 100")]
    [InlineData("/gpu-nvidia/0/temperature/0", SensorKind.Temperature, 80.0, 88.0, 1.0, 5.0, "30 95|0|100 °C#=#80 88")]
    [InlineData("/lpc/it8689e/0/temperature/0", SensorKind.Temperature, 70.0, 80.0, 1.0, 5.0, "20 85|0|100 °C#=#70 80")]
    [InlineData("/amdcpu/0/load/0", SensorKind.Load, null, null, 1.0, 5.0, "0 100|0|100 %#=#80 90")]
    [InlineData("/lpc/it8689e/0/control/0", SensorKind.Control, 85.0, null, 1.0, 5.0, "0 100|0|100 %#= /lpc/it8689e/0/fan/0#85 90")]
    [InlineData("/lpc/it8689e/0/fan/0", SensorKind.Fan, null, null, 50.0, 100.0, "0 1500|0|10,000 RPM#= /lpc/it8689e/0/control/0#1250 1450")]
    [InlineData("/amdcpu/0/power/0", null, null, null, 0.0, 0.0, "0 100|##")]
    [InlineData("/amdcpu/0/clock/1", null, null, null, 0.0, 0.0, "0 10000|##")]
    [InlineData("/ram/data/0", null, null, null, 0.0, 0.0, "0 50|##")]
    [InlineData($"{MadeUpHardware.Samsung}/temperature/0", SensorKind.Temperature, 70.0, 80.0, 1.0, 5.0, "40 85|0|100 °C#=#70 80")]
    [InlineData($"{MadeUpHardware.Ethernet}/throughput/8", SensorKind.Throughput, null, null, 100e3, 1e6, "0 100000000|0|100 GB/s#=#85100000 96200000")]
    [InlineData($"{MadeUpHardware.Samsung}/load/53", SensorKind.Load, null, null, 1.0, 5.0, "0 100|0|100 %#=#80 90")]
    [InlineData($"{MadeUpHardware.Battery}/level/0", SensorKind.Load, null, null, 1.0, 5.0, "0 100|0|100 %#=#80 90")]
    [InlineData($"{MadeUpHardware.Battery}/timespan/0", null, null, null, 0.0, 0.0, "0 0|##")]
    public void ThresholdsAndTheChartFollowTheMetersType(
        string sensor,
        SensorKind? kind,
        double? warning,
        double? critical,
        double step,
        double push,
        string chart)
    {
        var (lines, meters, added) = (chart.Split('#')[0], chart.Split('#')[1], chart.Split('#')[2]);
        var (range, ends) = (lines.Split('|')[0], lines.Split('|')[1..]);
        var snapshot = WithIntel;
        var value = snapshot.Sensors.First(each => each.Id == sensor).Value!.Value;

        var scale = MeterChart.Of(sensor, snapshot);

        Assert.Equal(kind, scale?.Kind);
        Assert.Equal(meters.Replace("=", sensor, StringComparison.Ordinal), Offered(new ItemConfig { Sensor = sensor }, snapshot));
        if (kind == SensorKind.Fan)
        {
            // The CPU fan made from its metric is offered its speed and the CPU fan's duty.
            Assert.Equal($"{Metrics.CpuFan.Id} {Metrics.CpuFanDuty.Id}", Offered(new ItemConfig { Metric = Metrics.CpuFan.Id }, snapshot));
            // Combined, it meters only what it shows.
            Assert.Equal(Metrics.CpuFan.Id, Offered(new ItemConfig { Metric = Metrics.CpuFan.Id, Sensors = [sensor, sensor] }, snapshot));
        }

        if (sensor == SensorMap.Chosen(snapshot, new OverlayConfig())[Metrics.RamUsed.Id]?.Id)
        {
            // The RAM in use, made from its metric, has no lines in GB that suit every PC, so it is judged by the RAM's load.
            Assert.Equal(Metrics.RamLoad.Id, Offered(new ItemConfig { Metric = Metrics.RamUsed.Id }, snapshot));
        }

        if (scale is null)
        {
            // An item that cannot be metered still charts its own reading, without lines, from 0 to its highest plus 15 %,
            // up to a 1-2-5 step; a reading that is no number, as a time, charts nothing.
            var reading = MeterChart.OfReading(sensor, snapshot);
            var (start, end) = reading is null ? default : MeterChart.Range(reading, [value]);
            Assert.Equal(range, string.Create(CultureInfo.InvariantCulture, $"{start} {end}"));
            Assert.True(reading is null or { Warning: null, Critical: null });
            return;
        }

        Assert.Equal((warning, critical, step, push), (scale.Warning, scale.Critical, scale.Step, scale.Push));
        var (from, to) = MeterChart.Range(scale, [value]);
        Assert.Equal(range, string.Create(CultureInfo.InvariantCulture, $"{from} {to}"));

        // A line added alone comes at its default, else near the top of a track of a fixed top, else above the readings.
        var (adds, addsHigh) = (MeterChart.Added(scale, null, ChartLine.Warning, [value]), MeterChart.Added(scale, null, ChartLine.Critical, [value]));
        Assert.Equal(added, string.Create(CultureInfo.InvariantCulture, $"{adds} {addsHigh}"));
        Assert.Equal(LevelsWithLines(warning, critical), Bands(scale));
        Assert.Equal((value, value, value), MeterChart.Summary([value, null, double.NaN]));

        // Cold review 4 (Q16): the chart spans from its first reading, a gap after it kept, and says how long ago that was.
        double?[] read = [.. Enumerable.Repeat<double?>(null, 470), value, null, .. Enumerable.Repeat<double?>(value, 128)];
        var (span, since) = MeterChart.Span(read, InEnglish);
        Assert.Equal(read[470..], span);
        Assert.Equal(Format(ChartText.MinutesAgo, 2), since);
        var (one, now) = MeterChart.Span([null, value], InEnglish);
        Assert.Equal([value], one);
        Assert.Equal(Format(ChartText.SecondsAgo, 1), now);

        // A point says its second's reading as the item words it, or none in a gap, and how long ago it came: to the second
        // where the axis rounds, the first reading 2 min 9 s ago where the axis says 2 min.
        var points = MeterChart.Points(span, reading => $"{reading}!", InEnglish);
        static string Point(string reading, string ago)
        {
            return Format(ChartText.Point, reading, ago);
        }

        Assert.Equal(
            [
                Point($"{value}!", Format(ChartText.MinutesSecondsAgo, 2, 9)),
                Point(Texts.English.Of(ChartText.NoReading), Format(ChartText.MinutesSecondsAgo, 2, 8)),
                Point($"{value}!", Format(ChartText.MinutesAgo, 2)),
                Point($"{value}!", Format(ChartText.SecondsAgo, 59)),
                Point($"{value}!", Texts.English.Of(UiText.ChartNow)),
            ],
            new[] { 0, 1, 9, 70, 129 }.Select(index => points[index]));
        Assert.Equal(

            [null, null, Format(ChartText.OutOfRange, ends), Format(ChartText.NotANumber, Number(scale, critical ?? warning ?? scale.Max))],
            new[] { "", scale.Max.ToString(English), (scale.Max + 1).ToString(English), "ninety" }.Select(typed => MeterChart.Check(scale, typed, InEnglish)));
        if (warning is { } low && critical is { } high)
        {
            // Pushed up to the track's end at most; a critical already there leaves the warning no room past it.
            var past = Math.Min(high + 1, scale.Max);
            var pushedUp = past > high ? new MovedLines(past, Math.Min(past + push, scale.Max), ChartLine.Critical) : new MovedLines(past, high, null);
            var pushedDown = MeterChart.Pushed(scale, low, low - 1, ChartLine.Critical);
            Assert.Equal(pushedUp, MeterChart.Pushed(scale, past, high, ChartLine.Warning));
            Assert.Equal(new MovedLines(low - 1 - push, low - 1, ChartLine.Warning), pushedDown);
            Assert.Equal(
                Format(ChartText.WarningPushed, ReadingFormatter.Format(new Reading(scale.Kind, low - 1 - push), English)),
                MeterChart.PushText(scale, pushedDown, InEnglish));
            Assert.Null(MeterChart.PushText(scale, MeterChart.Pushed(scale, low, high, ChartLine.Warning), InEnglish));
        }

        // The lowest line reached for more than half of the ten minutes fires most of the time; half of them does not.
        if ((warning ?? critical) is { } lowest)
        {
            Assert.Equal(
                (false, true),
                (
                    MeterChart.FiresMostly([.. Enumerable.Repeat<double?>(lowest, 300), null, lowest - 1], warning, critical),
                    MeterChart.FiresMostly([.. Enumerable.Repeat<double?>(lowest, 301), null], warning, critical)));
        }

        if (kind == SensorKind.Temperature)
        {
            // In °F the box takes 32 to 212, and a pushed line says where it went in °F.
            var fahrenheit = InEnglish with { Temperature = TemperatureUnit.Fahrenheit };
            Assert.Equal(
                [null, Format(ChartText.OutOfRange, "32", "212 °F")],
                new[] { "212", "213" }.Select(typed => MeterChart.Check(scale, typed, fahrenheit)));
            Assert.Equal(Format(ChartText.CriticalPushed, "212 °F"), MeterChart.PushText(scale, new MovedLines(97, 100, ChartLine.Critical), fahrenheit));
        }
    }

    private static string Format(ChartText text, params object[] values) => Texts.English.Format(English, text, values);

    // What the item can warn by: its own reading where lines judge it, then the others.
    private static string Offered(ItemConfig item, Snapshot snapshot) =>
        string.Join(
            ' ',
            (Threshold.Judges(item) ? [Threshold.Own(item)] : Array.Empty<WarnByConfig>())
                .Concat(Threshold.WarnByOf(item, Core.Editing.ItemEdits.KindOf(item, snapshot), snapshot))
                .Select(reading => reading.Sensor ?? reading.Metric));

    private static string Number(MeterScale scale, double value) => ReadingFormatter.Format(new Reading(scale.Kind, value), English, units: false);

    // Normal from 0, warning from the warning and critical from the critical, each line's own value included, over the
    // whole track; a meter without lines is normal all along.
    private static (double At, Level Level)[] LevelsWithLines(double? warning, double? critical) =>
    [
        (0, Level.Normal),
        .. warning is { } low ? new[] { (low, Level.Warning) } : [],
        .. critical is { } high ? new[] { (high, Level.Critical) } : [],
    ];

    private static (double At, Level Level)[] Bands(MeterScale scale) => [.. MeterChart.Stops(0, scale.Max, scale.Warning, scale.Critical)];
}
