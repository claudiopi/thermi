using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The fan finder replays real recorded series, including a fan that follows software.
/// </summary>
public sealed partial class FanFinderTests
{
    private const string BoardFan = "/lpc/it8689e/0/fan/0";
    private const string SecondFan = "/lpc/it8689e/0/fan/1";
    private const string CpuTotal = "/amdcpu/0/load/0";
    private const string CpuTemperature = "/amdcpu/0/temperature/2";
    private const string CpuPower = "/amdcpu/0/power/0";
    private const string GpuFan = "/gpu-nvidia/0/fan/1";
    private const string IntelFan = "/lpc/nct6798d/0/fan/1";

    private static readonly TimeSpan Half = TimeSpan.FromSeconds(0.5);

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    // Seconds of readings a series keeps coming after its recording, for the fans to be watched once the load ends.
    private static readonly int Watched = (int)FanFinder.Afterwards.TotalSeconds;

    /// <summary>
    /// Fan-control software drives the fan, late: it rises about 166 RPM under 30 s of full load, and about 250 more in the
    /// 10 s after it. Once the recording ends, its readings go on for the fans to be watched, the fan falling back.
    /// </summary>
    private static List<Snapshot> Software
    {
        get
        {
            var recorded = TestFiles.Series(TestRecordings.CpuLoad);
            return FallingBack([.. recorded, .. Enumerable.Repeat(recorded[^1], Watched)], recorded.Count, BoardFan, 200);
        }
    }

    private static int Recorded => Software.Count - Watched;

    /// <summary>The real series with the board's fan held at its speed before the load from then on: no fan answers.</summary>
    private static List<Snapshot> Unanswered =>
        Derived(Software, BoardFan, (snapshot, index) => Value(index < LoadStart ? snapshot : Software[LoadStart - 1], BoardFan));

    /// <summary>The board drives the fan: it rises from about 740 to 1,490 RPM a few seconds into the load.</summary>
    private static IReadOnlyList<Snapshot> Intel => TestFiles.Series(TestRecordings.IntelCpuLoad);

    private static int LoadStart => LoadStartOf(Software);

    // As the design says, the load starts at the first second whose CPU load passes 50 %.
    private static int LoadStartOf(IReadOnlyList<Snapshot> series) =>
        series.ToList().FindIndex(snapshot => snapshot.Sensors.Single(sensor => sensor.Name == "CPU Total").Value > 50);

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
        var series = Unanswered;
        var withoutTheLastLoadedReading = Unanswered.Select((snapshot, index) => index == LoadStart + 29 ? null : snapshot).ToList();

        var run = Replay(series);
        var checkedAtTheEnd = Replay(withoutTheLastLoadedReading);

        Assert.Equal((3.0, 33.0), (run.LoadFrom, run.LoadTo));
        Assert.Equal((FanFinderOutcome.NoFanRose, 3.0, 33.0), (checkedAtTheEnd.Result.Outcome, checkedAtTheEnd.LoadFrom, checkedAtTheEnd.LoadTo));
    }

    // Night QA: on the owner's PC the readings paused a few seconds as the load started, and the finder gave up on a fan
    // that answered. A pause leaves the averages fewer readings, no less true; only readings gone past MaxGap stop it.
    [Fact]
    public void OnlyALongGapInTheReadingsStopsTheFinder()
    {
        var paused = ThreeTimesFaster(Software).Select((snapshot, index) => index >= LoadStart && index < LoadStart + 4 ? null : snapshot).ToList();
        var cut = Unanswered.Select((snapshot, index) => index is >= 15 and < 25 ? null : snapshot).ToList();
        var time = new FakeTimeProvider();
        var silent = new FanFinder(Desktop, time);

        var answered = Replay(paused);
        var run = Replay(cut);
        time.Advance(FanFinder.MaxGap + Half);
        silent.Add(Desktop);

        Assert.Equal(new FanFinderResult(FanFinderOutcome.Found, BoardFan), answered.Result);

        // The last reading before the gap is at 13 s, and the readings come back at 24 s.
        Assert.Equal((FanFinderOutcome.ReadingsStopped, 3.0, 22.0), (run.Result.Outcome, run.LoadFrom, run.LoadTo));
        Assert.Equal((FanFinderOutcome.ReadingsStopped, false), (silent.Result?.Outcome, silent.Loading));
    }

    // Night QA 3: on the owner's PC the CPU fan rose 4 to 12 s after the load ended, and the readings stopped under the
    // load for seconds at a time. The finder watches the fans for a while after the load, which a long gap ends early too.
    [Fact]
    public void AFanThatAnswersOnlyAfterTheLoadIsStillFound()
    {
        var loadEnd = LoadStart + 29;
        var late = Replay(AnsweringAt(Software, loadEnd + 5));
        var gap = AnsweringAt(Software, LoadStart + 16).Select((snapshot, index) => index > LoadStart + 2 && index < LoadStart + 13 ? null : snapshot).ToList();
        var stalled = Replay(gap);

        Assert.Equal((new FanFinderResult(FanFinderOutcome.Found, BoardFan), 3.0, 33.0), (late.Result, late.LoadFrom, late.LoadTo));

        // The last reading before the gap is at 6 s, so the load ends at the first check past 8.5 s on; the fan answers at 20 s.
        Assert.Equal((new FanFinderResult(FanFinderOutcome.Found, BoardFan), 3.0, 15.0), (stalled.Result, stalled.LoadFrom, stalled.LoadTo));
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
    // outcome whose words mention fan-control software and picking the fan by hand. "Use it", which changes the sensor of
    // the item the finder ran from, keeps the item's meter and adds no item, as one undo step.
    // On the Intel desktop's recorded series the board drives its CPU fan, and the Nuvoton's second input answers the load,
    // the one the CPU fan item reads already. On the Ryzen's, software drives it and it answers after the load.
    [Theory]
    [InlineData("board-answers", "Found")]
    [InlineData("three-times-faster", "Found")]
    [InlineData("answering-at-once", "Found")]
    [InlineData("software", "Found")]
    [InlineData("falling-after-a-run", "Found")]
    [InlineData("rising-a-third", "Found")]
    [InlineData("held-from-a-load", "Found")]
    [InlineData("unanswered", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("heated-unanswered", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("already-hot", nameof(FanFinderOutcome.LoadDidNotHeatTheCpu))]
    [InlineData("board-jitter", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("gpu-fan-answers", nameof(FanFinderOutcome.NoFanRose))]
    [InlineData("two-alike", nameof(FanFinderOutcome.TwoRoseAlike))]
    [InlineData("two-alike-falling-apart", nameof(FanFinderOutcome.TwoRoseAlike))]
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

        var intel = series.StartsWith("board-", StringComparison.Ordinal);
        var result = (intel ? Replay(series == "board-jitter" ? BoardJitter : Intel, LoadStartOf(Intel))
            : series == "falling-after-a-run" ? Replay(FallingAfterARun(), loadStart: 4)
            : Replay(SeriesOf(series))).Result;
        var fan = intel ? IntelFan : BoardFan;
        Assert.Equal(expected, result.Outcome.ToString());
        Assert.Equal(expected == nameof(FanFinderOutcome.Found) ? fan : null, result.Fan);
        if (result.Fan is null)
        {
            return;
        }

        var pc = intel ? Intel[^1] : Desktop;
        Assert.Equal(fan, SensorMap.Chosen(pc, new OverlayConfig())[Metrics.CpuFan.Id]?.Id);
        var history = new UndoHistory();
        var config = new ThermiConfig();
        var used = Done(history, config, current => ItemEdits.ChangeSensor(current, Metrics.CpuFan.Id, pc, result.Fan));
        Assert.Equal(Item(config, Metrics.CpuFan.Id) with { Sensor = fan, Metric = null, Auto = Metrics.CpuFan.Id }, Item(used, Metrics.CpuFan.Id));
        Assert.Equal(Ids(config.Overlays[0]), Ids(used.Overlays[0]));
        Assert.Equal(File(config), File(history.Undo(used)!.Config));
        Assert.False(history.CanUndo);
    }

    private static List<Snapshot> SeriesOf(string name) => name switch
    {
        "three-times-faster" => ThreeTimesFaster(Software),
        "answering-at-once" => AnsweringAt(Software, LoadStart),
        "rising-a-third" => RisingAThird,
        "held-from-a-load" => HeldFromALoad,

        "unanswered" => Unanswered,
        "already-hot" => AlreadyHot(heated: false),
        "heated-unanswered" => AlreadyHot(heated: true),

        // A GPU's fan never cools the CPU, however it answers a load that also warms the GPU.
        "gpu-fan-answers" => Derived(Unanswered, GpuFan, (_, index) => 3 * Value(Software[index], BoardFan)),
        "two-alike" => Derived(ThreeTimesFaster(Software), SecondFan, (snapshot, _) => Value(snapshot, BoardFan) * 0.6),

        // Two that rose alike stay alike, however unlike they fall once the load ends: the second falls 50 RPM a second.
        "two-alike-falling-apart" => FallingBack(SeriesOf("two-alike"), Recorded, SecondFan, 50),
        "light-load" => Derived(Unanswered, CpuTotal, (snapshot, _) => Value(snapshot, CpuTotal) * 0.75),
        "unread-load" => Derived(Unanswered, CpuTotal, (_, _) => null),
        _ => Software,
    };

    private sealed record Run(FanFinderResult Result, double? LoadFrom, double? LoadTo);

    // The finder starts on the fourth reading before the recorded load, so its 3 s baseline ends on the last reading
    // before it and the window's load lines up with the recorded one. A reading comes on each whole second, a null
    // being one that never came, and the window checks on each half second, past the series' end too.
    private static Run Replay(IReadOnlyList<Snapshot?> series) => Replay(series, LoadStart);

    private static Run Replay(IReadOnlyList<Snapshot?> series, int loadStart)
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();
        var finder = new FanFinder(series[loadStart - 4]!, time);
        double? from = null;
        double? to = null;
        for (var index = loadStart - 3; finder.Result is null && index < series.Count + 60; index++)
        {
            foreach (var reading in new[] { null, series.ElementAtOrDefault(index) })
            {
                time.Advance(Half);
                Check(finder, reading);
                var at = time.GetElapsedTime(start).TotalSeconds;
                from ??= finder.Loading ? at : null;
                to ??= from is not null && !finder.Loading ? at : null;
            }
        }

        return new Run(finder.Result ?? throw new InvalidOperationException("The series ended before the finder did."), from, to);
    }

    private static void Check(FanFinder finder, Snapshot? reading)
    {
        if (reading is null)
        {
            finder.Tick();
        }
        else
        {
            finder.Add(reading);
        }
    }

    /// <summary>
    /// Derived from the real series, since fan-control software holds the fan where it left it even when closed: the board's fan spins
    /// three times as fast, so it answers the load as clearly as a fan the board drives.
    /// </summary>
    private static List<Snapshot> ThreeTimesFaster(IReadOnlyList<Snapshot> series) =>
        Derived(series, BoardFan, (snapshot, _) => 3 * Value(snapshot, BoardFan));

    /// <summary>
    /// Derived as <see cref="ThreeTimesFaster"/>, but from the load on it keeps its speed from before it until the reading
    /// <paramref name="from"/>, during the load or after it, and then jumps to its last loaded speed.
    /// </summary>
    private static List<Snapshot> AnsweringAt(List<Snapshot> series, int from)
    {
        var loadEnd = LoadStart + 29;
        return Derived(series, BoardFan, (snapshot, index) => 3 * Value(
            index < LoadStart ? snapshot : series[index >= from ? loadEnd : LoadStart - 1],
            BoardFan));
    }

    /// <summary>
    /// QA round 4: started again right after a run, the fan was still falling from 847 RPM, and the 3 s before the load
    /// averaged so high that its rise from 543 to 800 read as none. Here <see cref="ThreeTimesFaster"/> comes after 6 s in
    /// which the fan falls 200 RPM a second, and the finder starts as it begins to fall.
    /// </summary>
    private static List<Snapshot> FallingAfterARun()
    {
        var series = ThreeTimesFaster(Software);
        var settled = Value(series[0], BoardFan)!.Value;
        return [.. Derived([.. Enumerable.Repeat(series[0], 6)], BoardFan, (_, index) => settled + ((6 - index) * 200)), .. series];
    }

    /// <summary>
    /// The Intel desktop's series with its CPU fan at its idle speeds, the 10 s before the load over and over, starting so the
    /// 3 s before the load are the lowest: its 3 s averages then jitter up to 85 RPM and 12 % over them, which is no rise.
    /// Its last second repeats for the fans to be watched after the load.
    /// </summary>
    private static List<Snapshot> BoardJitter
    {
        get
        {
            var start = LoadStartOf(Intel) - 3;
            return Derived([.. Intel, .. Enumerable.Repeat(Intel[^1], Watched)], IntelFan, (_, at) => Value(Intel[(((at - start) % 10) + 10) % 10], IntelFan));
        }
    }

    /// <summary>
    /// The series with <paramref name="fan"/> falling back from the reading <paramref name="ended"/> on, by
    /// <paramref name="rpm"/> a second, to its speed before the load: a fan never holds where the load left it.
    /// </summary>
    private static List<Snapshot> FallingBack(List<Snapshot> series, int ended, string fan, double rpm)
    {
        var before = Value(series[LoadStartOf(series) - 1], fan)!.Value;
        var last = Value(series[ended - 1], fan)!.Value;
        return Derived(series, fan, (snapshot, index) => index < ended ? Value(snapshot, fan) : Math.Max(before, last - ((index - ended + 1) * rpm)));
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
