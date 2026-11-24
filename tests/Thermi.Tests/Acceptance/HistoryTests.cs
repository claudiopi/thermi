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

        var levels = history.Histogram(Cpu, 10);

        Assert.Equal([120, 0, 0, 0, 0, 0, 0, 0, 0, 360], levels);
        Assert.Equal(Enumerable.Repeat(0, 4), history.Histogram("gpu.temperature", 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Histogram(Cpu, 0));
    }

    [Fact]
    public void AReadingKeepsItsSecondThoughItArrivesLateOrEarly()
    {
        var time = new FakeTimeProvider();
        var history = new SensorHistory(time);
        double?[] values = [20, 30, null, -5, 150];
        TimeSpan[] gaps = [TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(0.7), TimeSpan.FromSeconds(1.3), TimeSpan.FromSeconds(0.2)];

        for (var index = 0; index < values.Length; index++)
        {
            history.Add(new Dictionary<string, Reading>(StringComparer.Ordinal) { [Cpu] = new(SensorKind.Temperature, values[index] ?? double.NaN) });
            if (index < gaps.Length)
            {
                time.Advance(gaps[index]);
            }
        }

        Assert.Equal([20, 30, null, 150], history.LastMinute(Cpu));
        Assert.Equal([1, 1, 0, 1], history.Histogram(Cpu, 4));
    }

    // The inspector's histogram is of what the meter shows, an item's offset included, and says the range it spans.
    [Fact]
    public void TheHistogramTakesAnOffsetAndTheRangeSpansTheKeptMinutes()
    {
        var time = new FakeTimeProvider();
        var history = new SensorHistory(time);
        Assert.Null(history.Range(Cpu));

        Run(history, time, [40, 70, 55]);

        Assert.Equal((40, 70), history.Range(Cpu));
        Assert.Equal([0, 0, 2, 1], history.Histogram(Cpu, 4, offset: 10));
        Assert.Equal([0, 0, 0, 3], history.Histogram(Cpu, 4, offset: 100));
        time.Advance(SensorHistory.Kept);
        Assert.Null(history.Range(Cpu));
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
