using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Sensor history on the injected clock: the last minute and ten minutes with gaps.
/// </summary>
public sealed class HistoryTests
{
    private const string Cpu = "cpu.temperature";

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    [Fact]
    public void TheSparklineKeepsTheLastMinute()
    {
        var time = new FakeTimeProvider();
        var history = new SensorHistory(time);
        Assert.Empty(history.LastMinute(Cpu));

        Run(history, time, Enumerable.Range(0, 120).Select(second => (double)second));

        Assert.Equal(Enumerable.Range(60, 60).Select(second => (double?)second), history.LastMinute(Cpu));

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal([.. Enumerable.Range(90, 30).Select(second => (double?)second), .. Enumerable.Repeat<double?>(null, 30)], history.LastMinute(Cpu));
        Assert.Equal(Enumerable.Repeat<double?>(null, 60), history.LastMinute("gpu.temperature"));
    }

    [Fact]
    public void TheHistoryKeepsTenMinutesAndLeavesGapsEmpty()
    {
        var time = new FakeTimeProvider();
        var history = new SensorHistory(time);

        Run(history, time, Enumerable.Repeat(5.0, 4 * 60));
        time.Advance(TimeSpan.FromMinutes(2) + Second);
        Run(history, time, Enumerable.Repeat(95.0, 6 * 60));

        var kept = history.TenMinutes(Cpu);

        Assert.Equal([.. Enumerable.Repeat<double?>(5, 120), .. Enumerable.Repeat<double?>(null, 120), .. Enumerable.Repeat<double?>(95, 360)], kept);
        Assert.All(history.TenMinutes("gpu.temperature"), Assert.Null);
    }

    // Readings, "-" for none, with the seconds between their arrivals, and the last minute kept. A reading within half a
    // second of the last replaces it; but one that arrives late, a second skipped, and the next soon after, as under a full
    // load (night QA: the chart went blank for 15 s while the list kept changing), each keep a second of their own.
    [Theory]
    [InlineData("20 30 - -5 150", "1.4 0.7 1.3 0.2", "20 30 - 150")]
    [InlineData("1 2 3 4 5 6", "1.6 0.4 1.6 0.4 1", "1 2 3 4 5 6")]
    [InlineData("1 2 3 4 5", "3.6 0.1 0.1 1", "1 - 2 3 4 5")]
    public void AReadingKeepsItsSecondThoughItArrivesLateOrEarly(string read, string apart, string kept)
    {
        var time = new FakeTimeProvider();
        var history = new SensorHistory(time);
        var values = read.Split(' ').Select(value => value == "-" ? double.NaN : double.Parse(value, CultureInfo.InvariantCulture)).ToList();
        var gaps = apart.Split(' ').Select(gap => TimeSpan.FromSeconds(double.Parse(gap, CultureInfo.InvariantCulture))).ToList();

        for (var index = 0; index < values.Count; index++)
        {
            history.Add(new Dictionary<string, Reading>(StringComparer.Ordinal) { [Cpu] = new(SensorKind.Temperature, values[index]) });
            if (index < gaps.Count)
            {
                time.Advance(gaps[index]);
            }
        }

        var shown = history.LastMinute(Cpu).Select(value => value is { } some ? some.ToString(CultureInfo.InvariantCulture) : "-");
        Assert.Equal(kept, string.Join(' ', shown));
    }

    // One reading a second, the first one now.
    private static void Run(SensorHistory history, FakeTimeProvider time, IEnumerable<double> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                time.Advance(Second);
            }

            first = false;
            history.Add(new Dictionary<string, Reading>(StringComparer.Ordinal) { [Cpu] = new(SensorKind.Temperature, value) });
        }
    }
}
