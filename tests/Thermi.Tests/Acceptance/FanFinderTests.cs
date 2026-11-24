using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The fan finder replays real recorded series, including a fan that follows software.
/// </summary>
public sealed class FanFinderTests
{
    private const string BoardFan = "/lpc/it8689e/0/fan/0";
    private const string SecondFan = "/lpc/it8689e/0/fan/1";
    private const string CpuTotal = "/amdcpu/0/load/0";
    private const string GpuFan = "/gpu-nvidia/0/fan/1";

    private static readonly TimeSpan Half = TimeSpan.FromSeconds(0.5);

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    /// <summary>The fan barely answers: fan-control software drives it, and it rises about 166 RPM under 30 s of full load.</summary>
    private static IReadOnlyList<Snapshot> Software => TestFiles.Series("ryzen-9950x-rtx-5060-ti-cpu-load-fan-software.jsonl");

    // As the design says, the load starts at the first second whose CPU load passes 50 %.
    private static int LoadStart => Software.ToList().FindIndex(snapshot => snapshot.Sensors.Single(sensor => sensor.Id == CpuTotal).Value > 50);

    [Fact]
    public void OnDerivedSeriesTheFinderStopsThreeSecondsAfterAFanMeetsTheRuleOrAtEightSeconds()
    {
        var slow = Replay(ThreeTimesFaster(Software));
        var fast = Replay(AnsweringAt(Software, LoadStart));
        var late = Replay(AnsweringAt(Software, LoadStart + 27));

        // The slow one's last 3 s first average 2,092 RPM against 1,720 before the load at 13 s, 10 s into the load.
        Assert.Equal((new FanFinderResult(FanFinderOutcome.Found, BoardFan), 3.0, 16.0), (slow.Result, slow.LoadFrom, slow.LoadTo));
        // The fast one meets the rule as soon as there are 3 s of load to average, and runs on to the minimum.
        Assert.Equal((new FanFinderResult(FanFinderOutcome.Found, BoardFan), 3.0, 11.0), (fast.Result, fast.LoadFrom, fast.LoadTo));
        // The late one meets the rule a second before the longest run ends, and the last 3 s still pick it.
        Assert.Equal((new FanFinderResult(FanFinderOutcome.Found, BoardFan), 3.0, 33.0), (late.Result, late.LoadFrom, late.LoadTo));
    }

    [Fact]
    public void WhenNoFanMeetsTheRuleTheLoadRunsThirtySeconds()
    {
        var series = Software.ToList();
        var withoutTheLastLoadedReading = Software.Select((snapshot, index) => index == LoadStart + 29 ? null : snapshot).ToList();

        var run = Replay(series);
        var checkedAtTheEnd = Replay(withoutTheLastLoadedReading);

        Assert.Equal((3.0, 33.0), (run.LoadFrom, run.LoadTo));
        Assert.Equal((FanFinderOutcome.NoFanRose, 3.0, 33.0), (checkedAtTheEnd.Result.Outcome, checkedAtTheEnd.LoadFrom, checkedAtTheEnd.LoadTo));
    }

    [Fact]
    public void AGapInTheReadingsStopsTheFinder()
    {
        var cut = Software.Select((snapshot, index) => index is >= 15 and < 20 ? null : snapshot).ToList();
        var time = new FakeTimeProvider();
        var silent = new FanFinder(Desktop, time);

        var run = Replay(cut);
        time.Advance(TimeSpan.FromSeconds(3));
        silent.Add(Desktop);

        // The last reading before the gap is at 13 s, and the readings come back at 19 s.
        Assert.Equal((FanFinderOutcome.ReadingsStopped, 3.0, 16.0), (run.Result.Outcome, run.LoadFrom, run.LoadTo));
        Assert.Equal((FanFinderOutcome.ReadingsStopped, false), (silent.Result?.Outcome, silent.Loading));
    }

    // The window shows which fan rises most while the finder runs, before any meets the rule.
    [Fact]
    public void WhileItRunsTheFinderSaysWhichFanRisesMost()
    {
        var series = ThreeTimesFaster(Software);
        var time = new FakeTimeProvider();
        var finder = new FanFinder(series[LoadStart - 4], time);
        var rising = new List<FanRise?>();
        foreach (var snapshot in series.Skip(LoadStart - 3).Take(8))
        {
            time.Advance(TimeSpan.FromSeconds(1));
            finder.Add(snapshot);
            rising.Add(finder.Rising);
        }

        Assert.All(rising.Take(3), Assert.Null);
        Assert.Equal(BoardFan, rising[^1]?.Fan);
        Assert.True(rising[^1]!.Rpm > 0);
        Assert.Equal(rising[^1]!.Rpm / Value(series[LoadStart - 1], BoardFan)!.Value, rising[^1]!.Share, 0.1);
        Assert.Null(finder.Result);
    }

    [Fact]
    public void TheFinderCanBeCancelledAtAnyTime()
    {
        var time = new FakeTimeProvider();
        var finder = new FanFinder(Software[0], time);
        foreach (var snapshot in Software.Skip(1).Take(5))
        {
            time.Advance(TimeSpan.FromSeconds(1));
            finder.Add(snapshot);
        }

        var loadingBefore = finder.Loading;
        finder.Cancel();
        time.Advance(TimeSpan.FromSeconds(1));
        finder.Add(Software[6]);
        finder.Cancel();

        Assert.True(loadingBefore);
        Assert.Equal((new FanFinderResult(FanFinderOutcome.Cancelled), false), (finder.Result, finder.Loading));
    }

    // A series, and what the finder gives: Found, an outcome when it picks nothing, or why it cannot start. NoFanRose is the
    // outcome whose words mention fan-control software and picking the fan by hand. "Use it" keeps the item's meter and
    // adds no item, as one undo step.
    [Theory]
    [InlineData("three-times-faster", "Found")]
    [InlineData("answering-at-once", "Found")]
    [InlineData("software", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("gpu-fan-answers", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("two-alike", nameof(FanFinderOutcome.TwoRoseAlike))]
    [InlineData("light-load", nameof(FanFinderOutcome.LoadDidNotReachTheCpu))]
    [InlineData("unread-load", nameof(FanFinderOutcome.LoadDidNotReachTheCpu))]
    [InlineData("no-driver", nameof(FanFinderRefusal.DriverMayBeMissing))]
    [InlineData("no-board-fans", nameof(FanFinderRefusal.NoBoardFans))]
    public void TheFinderPicksTheFanThatAnswersOrSaysWhyNot(string series, string expected)
    {
        var start = series switch
        {
            "no-driver" => TestRecordings.Read(TestRecordings.NoAdmin),
            "no-board-fans" => new Snapshot([.. Desktop.Sensors.Where(sensor => !(sensor.HardwareType == "SuperIO" && sensor.Type == "Fan"))]),
            _ => null,
        };
        if (start is not null)
        {
            Assert.Equal(expected, FanFinder.CannotStart(start)?.ToString());
            Assert.Throws<ArgumentException>(() => new FanFinder(start, new FakeTimeProvider()));
            return;
        }

        var result = Replay(SeriesOf(series)).Result;
        Assert.Equal(expected, result.Outcome.ToString());
        Assert.Equal(expected == nameof(FanFinderOutcome.Found) ? BoardFan : null, result.Fan);
        if (result.Fan is null)
        {
            return;
        }

        var history = new UndoHistory();
        var config = new ThermiConfig();
        var used = Done(history, config, current => ItemEdits.UseCpuFan(current, 0, Desktop, result.Fan));
        Assert.Equal(Item(config, Metrics.CpuFan.Id) with { Sensor = BoardFan, Metric = null, Auto = Metrics.CpuFan.Id }, Item(used, Metrics.CpuFan.Id));
        Assert.Equal(Ids(config.Overlays[0]), Ids(used.Overlays[0]));
        Assert.Equal(File(config), File(history.Undo(used)!.Config));
        Assert.False(history.CanUndo);
    }

    private static IReadOnlyList<Snapshot> SeriesOf(string name) => name switch
    {
        "three-times-faster" => ThreeTimesFaster(Software),
        "answering-at-once" => AnsweringAt(Software, LoadStart),

        // A GPU's fan never cools the CPU, however it answers a load that also warms the GPU.
        "gpu-fan-answers" => Derived(Software, GpuFan, (snapshot, _) => 3 * Value(snapshot, BoardFan)),
        "two-alike" => Derived(ThreeTimesFaster(Software), SecondFan, (snapshot, _) => Value(snapshot, BoardFan) * 0.6),
        "light-load" => Derived(Software, CpuTotal, (snapshot, _) => Value(snapshot, CpuTotal) * 0.75),
        "unread-load" => Derived(Software, CpuTotal, (_, _) => null),
        _ => Software,
    };

    private sealed record Run(FanFinderResult Result, double? LoadFrom, double? LoadTo);

    // The finder starts on the fourth reading before the recorded load, so its 3 s baseline ends on the last reading
    // before it and the window's load lines up with the recorded one. A reading comes on each whole second, a null
    // being one that never came, and the window checks on each half second.
    private static Run Replay(IReadOnlyList<Snapshot?> series)
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();
        var finder = new FanFinder(series[LoadStart - 4]!, time);
        double? from = null;
        double? to = null;
        for (var index = LoadStart - 3; index < series.Count && finder.Result is null; index++)
        {
            foreach (var reading in new[] { null, series[index] })
            {
                time.Advance(Half);
                if (reading is null)
                {
                    finder.Tick();
                }
                else
                {
                    finder.Add(reading);
                }

                var at = time.GetElapsedTime(start).TotalSeconds;
                from ??= finder.Loading ? at : null;
                to ??= from is not null && !finder.Loading ? at : null;
            }
        }

        return new Run(finder.Result ?? throw new InvalidOperationException("The series ended before the finder did."), from, to);
    }

    /// <summary>
    /// Derived from the real series, since fan-control software holds the fan where it left it even when closed: the board's fan spins
    /// three times as fast, so it answers the load as clearly as a fan the board drives.
    /// </summary>
    private static List<Snapshot> ThreeTimesFaster(IReadOnlyList<Snapshot> series) =>
        Derived(series, BoardFan, (snapshot, _) => 3 * Value(snapshot, BoardFan));

    /// <summary>
    /// Derived as <see cref="ThreeTimesFaster"/>, but during the load it keeps its speed from before it until the reading
    /// <paramref name="from"/>, and then jumps to its last loaded speed.
    /// </summary>
    private static List<Snapshot> AnsweringAt(IReadOnlyList<Snapshot> series, int from)
    {
        var loadEnd = LoadStart + 29;
        return Derived(series, BoardFan, (snapshot, index) => 3 * Value(
            index < LoadStart || index > loadEnd ? snapshot : series[index >= from ? loadEnd : LoadStart - 1],
            BoardFan));
    }

    private static List<Snapshot> Derived(IReadOnlyList<Snapshot> series, string id, Func<Snapshot, int, double?> value) =>
        [
            .. series.Select((snapshot, index) => new Snapshot(
            [
                .. snapshot.Sensors.Select(sensor => sensor.Id == id ? sensor with { Value = value(snapshot, index) } : sensor),
            ])),
        ];

    private static double? Value(Snapshot snapshot, string id) => snapshot.Sensors.Single(sensor => sensor.Id == id).Value;
}
