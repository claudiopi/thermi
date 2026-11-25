using System.Globalization;
using Thermi.Core.Editing;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Frames;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>From the presents the service traces to the FPS and frame time the overlay shows for the app in front.</summary>
public sealed class FrameTests
{
    private const int Game = 4242;

    private const string FpsSensor = "/game/0/framerate/0";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    // Zwift, an OpenGL game whose frames only the kernel's detailed history tells of, each repeated by a blit that is never
    // counted, beside a terminal that presents from a child of its own and an agent that presents through Direct3D 9. FPS
    // follows the game, not the focus: Zwift, maximized in front, becomes the game once it counts as presenting, from the
    // third second, and stays it while the terminal, on another monitor, takes the focus, maximized or not, at 18 FPS, and
    // while the desktop is in front. The terminal alone, not filling its monitor, never shows FPS.
    [Fact]
    public void TheOverlayShowsTheFramesOfTheAppInFront()
    {
        const int Zwift = 63632;
        const int Terminal = 27540;
        var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, FrameTimeItem] };
        (int? Front, bool Fills)[] fronts =
            [(Zwift, true), (Zwift, true), (Zwift, true), (Zwift, true), (Terminal, false), (Terminal, false), (Terminal, true), (null, false)];
        var (game, terminal) = (new FrontGame(), new FrontGame());

        var seconds = Replay(TestFiles.Presents(TestRecordings.ZwiftFrames)).Zip(fronts).Select(each => string.Join(
            " | ",
            new[] { game.Next(each.First, each.Second.Front, each.Second.Fills), terminal.Next(each.First, Terminal, false) }
                .Select(shown => Shown(overlay, Desktop, each.First, shown))
                .Select(shown => string.Join(" ", new[] { Metrics.Fps.Id, Metrics.FrameTime.Id }.Select(shown.GetValueOrDefault).OfType<string>()))));

        Assert.Equal(
            [
                " | ",
                " | ",
                "161 7.9 ms | ",
                "151 22.5 ms | ",
                "156 22.3 ms | ",
                "154 17.7 ms | ",
                "153 23.5 ms | ",
                "156 13.1 ms | ",
            ],
            seconds);
    }

    // A service that could not start its trace, and one from before, which sends no slots: the overlay keeps no room for
    // FPS, while the window, open, shows it as missing so it can be selected, and its catalog says frames cannot be read,
    // unlike while frames are read and nothing in front presents.
    [Fact]
    public void FramesThatCannotBeReadShowAsMissing()
    {
        var unread = Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of(null)] };

        foreach (var snapshot in new[] { unread, Desktop })
        {
            Assert.Null(Fps(OverlayConfig.First, snapshot, null, Game));
            Assert.Equal(ReadingFormatter.Missing, Fps(OverlayConfig.First, snapshot, null, Game, windowOpen: true));
            Assert.Equal(NoValue.FramesUnread, WhyFps(GameSensors.With(snapshot, Game)));
        }

        Assert.Equal(NoValue.NoFrames, WhyFps(GameSensors.With(Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of([])] }, Game)));
    }

    // A browser presents from a process it started, so the app in front counts the frames of a child, unless it presents
    // itself; a process in front that presents nothing and started nothing that does shows none.
    [Fact]
    public void AnAppInFrontCountsTheFramesOfWhatItStarted()
    {
        const int Child = 777;
        const int Other = 9000;
        FrameRate[] child = [new(Child, Game, 90, 11), new(Other, 1, 144, 7)];
        FrameRate[] both = [.. child, new(Game, 1, 60, 16.7)];
        (FrameRate[] Rates, int Front)[] fronts = [(child, Game), (both, Game), (child, 4243)];

        Assert.Equal(["90", "60", null], fronts.Select(each => Fps(OverlayConfig.First, Desktop, each.Rates, each.Front)));

        var game = GameSensors.With(Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of([])] }, null);
        var added = Edited.Done(ItemEdits.AddSensor(new ThermiConfig(), 0, game, FpsSensor)).Overlays[0];
        Assert.Equal("90", Shown(added, Desktop, child, Game).GetValueOrDefault(FpsSensor));
    }

    // FPS and frame time start with no lines, whether from a new file or the catalog, and a line added comes at 60 FPS or
    // 30 critical, at 50 ms or 100 critical, or a push beyond the other line. With lines, frames per second are in trouble
    // when low, a frame time when high, in the value and in each second of its line, a value at a line reaching it: 144 FPS
    // is fine, 60 a warning and 30 critical; 7 ms fine, 50 ms a warning and 100 ms critical; each on its own scale. A frame
    // rate's critical above its warning is refused.
    [Fact]
    public void FrameRateWarnsLowAndFrameTimeHigh()
    {
        var game = GameSensors.With(Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of([])] }, null);
        var added = Edited.Done(ItemEdits.AddSensor(ThermiConfig.New, 0, game, FpsSensor));
        Assert.All(
            [Edited.Item(ThermiConfig.New, Metrics.Fps.Id), Edited.Item(added, FpsSensor)],
            item => Assert.Null(item.Threshold));
        var (rate, wait) = (MeterChart.Of(Metrics.Fps.Id, game)!, MeterChart.Of(Metrics.FrameTime.Id, game)!);
        Assert.Equal(
            [60.0, 30, 55, 50, 100, 105],
            [
                MeterChart.Added(rate, null, ChartLine.Warning, []),
                MeterChart.Added(rate, null, ChartLine.Critical, []),
                MeterChart.Added(rate, new Threshold(60, null), ChartLine.Critical, []),
                MeterChart.Added(wait, null, ChartLine.Warning, []),
                MeterChart.Added(wait, null, ChartLine.Critical, []),
                MeterChart.Added(wait, new Threshold(100, null), ChartLine.Critical, []),
            ]);

        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        var overlay = OverlayConfig.First with
        {
            Items =
            [
                .. OverlayConfig.First.Items.Select(item => item.Id == Metrics.Fps.Id ? item with { Threshold = new Threshold(60, 30) } : item),
                FrameTimeItem,
            ],
        };
        IReadOnlyList<OverlayItem> shown = [];
        foreach (var (fps, frameTime) in new[] { (144.0, 7.0), (61, 40), (60, 50), (45, 99), (30, 100), (12, 200) })
        {
            var slots = FrameSlots.Of([new FrameRate(Game, 1, fps, frameTime)]);
            var snapshot = GameSensors.With(Desktop with { Sensors = [.. Desktop.Sensors, .. slots] }, Game);
            var readings = SensorMap.Read(snapshot, overlay);
            history.Add(overlay, readings);
            shown = OverlayLayout.Build(overlay, readings, English, history: history);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        GraphPoint?[] Line(string id)
        {
            return [.. shown.Single(item => item.Id == id).Graph!.TakeLast(6)];
        }

        Assert.Equal(
            [
                new GraphPoint(0.6, Level.Normal),
                new(61 / 240.0, Level.Normal),
                new(0.25, Level.Warning),
                new(45 / 240.0, Level.Warning),
                new(0.125, Level.Critical),
                new(0.05, Level.Critical),
            ],
            Line(Metrics.Fps.Id));
        Assert.Equal(
            [
                new GraphPoint(0.07, Level.Normal),
                new(0.4, Level.Normal),
                new(0.5, Level.Warning),
                new(0.99, Level.Warning),
                new(1, Level.Critical),
                new(1, Level.Critical),
            ],
            Line(Metrics.FrameTime.Id));
        Assert.Equal(Refusal.CriticalAboveWarning, ItemEdits.SetThreshold(ThermiConfig.New, Metrics.Fps.Id, 30, 60).Refused);
    }

    // An app counts as presenting only once it presented at least 10 frames a second for 3 seconds in a row: a window that
    // redraws now and then, as a terminal or thermi's own, shows no FPS at all, and a game shows its third second on.
    [Fact]
    public void OnlyAnAppThatPresentsSteadilyShowsFrames()
    {
        static IEnumerable<Present> Every(double milliseconds, double until)
        {
            return Enumerable.Range(0, (int)(until / milliseconds)).Select(at => new Present(Game, 1, 1 + (at * milliseconds)));
        }

        static string?[] Seconds(IEnumerable<Present> presents)
        {
            return [.. Replay([.. presents]).Take(5).Select(rates => Fps(OverlayConfig.First, Desktop, rates, Game))];
        }

        Assert.Equal([null, null, null, null, null], Seconds(Every(200, 6000)));
        Assert.Equal([null, null, "60", "60", "60"], Seconds(Every(1000 / 60.0, 6000)));
    }

    // What the overlay shows for FPS and frame time each second, from the presents the service traced, through the slots
    // its snapshot carries. A second's FPS is its presents over its length; its frame time the longest wait for a frame that
    // ended in it, or the one still going; a process that stops presenting reads 0 FPS for 5 s, then takes no room; a
    // present the trace delivers after its second closed is not counted in the next; presents delivered out of order are
    // counted in order. The kernel's presents, marked k, count only for a process that sends no runtime's, whose presents
    // the kernel repeats, and its detailed ones, marked d, only for one that sends neither. Each row comes after 3 s of
    // steady presents of its first source, so the app counts as presenting, and its times from half a millisecond after.
    [Theory]
    [InlineData("0 100 200 300 400 500 600 700 800 900 1000", "1000", "11 100.0 ms")]
    [InlineData("0 16 33 50 150 166", "1000", "6 834.0 ms")]
    [InlineData("0 100", "1000 2000", "2 900.0 ms|0 999.9 ms")]
    [InlineData("0 100", "1000 2000 3000 4000 5000 6000 7000", "2 900.0 ms|0 999.9 ms|0 999.9 ms|0 999.9 ms|0 999.9 ms||")]
    [InlineData("100 1050 950", "1000 2000", "1 900.0 ms|1 950.0 ms")]
    [InlineData("100 900 700 1200", "1000 2000", "3 600.0 ms|1 800.0 ms")]
    [InlineData("0 k2 500 k502", "1000", "2 500.0 ms")]
    [InlineData("k0 k500", "1000", "2 500.0 ms")]
    [InlineData("k0 d1 k500 d501 d900", "1000", "2 500.0 ms")]
    public void EachSecondCountsItsFramesAndItsLongestWait(string presents, string closes, string expected)
    {
        const double After = 3000.5;
        var tally = new FrameTally();
        var sent = presents.Split(' ')
            .Select(each => (
                At: After + double.Parse(each.TrimStart('k', 'd'), CultureInfo.InvariantCulture),
                Source: each[0] switch { 'k' => PresentSource.Kernel, 'd' => PresentSource.KernelDetailed, _ => PresentSource.Dxgi }))
            .ToList();
        for (var second = 1000.0; second <= 3000; second += 1000)
        {
            for (var at = second - 1000; at < second; at += 10)
            {
                tally.Add(new Present(Game, 1, at, sent[0].Source));
            }

            tally.Close(second);
        }

        var times = sent.Select(each => each.At).ToList();
        var seconds = new List<string>();
        var delivered = 0;
        foreach (var close in closes.Split(' ').Select(each => After + double.Parse(each, CultureInfo.InvariantCulture)))
        {
            // The trace delivers presents in the order the row gives, up to the first that is past the close.
            for (; delivered < times.Count && times[..(delivered + 1)].Max() <= close; delivered++)
            {
                tally.Add(new Present(Game, 1, times[delivered], sent[delivered].Source));
            }

            var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, FrameTimeItem] };
            var shown = Shown(overlay, Desktop, tally.Close(close), Game);
            seconds.Add(string.Join(" ", new[] { Metrics.Fps.Id, Metrics.FrameTime.Id }.Select(shown.GetValueOrDefault).OfType<string>()));
        }

        Assert.Equal(expected, string.Join("|", seconds));
    }

    // The presents a second at a time, through the tally the service runs, as the slots each second's snapshot carries.
    private static IEnumerable<IReadOnlyList<FrameRate>> Replay(IReadOnlyList<Present> presents)
    {
        var tally = new FrameTally();
        var end = presents.Max(present => present.At);
        for (var close = 1000.0; close <= end + FrameTally.KeptFor; close += 1000)
        {
            foreach (var present in presents.Where(present => present.At > close - 1000 && present.At <= close))
            {
                tally.Add(present);
            }

            yield return tally.Close(close);
        }
    }

    private static NoValue? WhyFps(Snapshot snapshot) =>
        SensorCatalog.Build(snapshot).SelectMany(device => device.Sensors).Single(row => row.Sensor.Type == nameof(SensorKind.FrameRate)).Why;

    private static ItemConfig FrameTimeItem =>
        new() { Id = Metrics.FrameTime.Id, Metric = Metrics.FrameTime.Id, Threshold = new Threshold(50, 100) };

    // What the overlay shows for FPS, or null when it keeps no room for it.
    private static string? Fps(OverlayConfig overlay, Snapshot snapshot, IReadOnlyList<FrameRate>? rates, int? front, bool windowOpen = false) =>
        Shown(overlay, snapshot, rates, front, windowOpen).GetValueOrDefault(Metrics.Fps.Id);

    // Each item the overlay keeps room for, with what it shows.
    private static Dictionary<string, string> Shown(
        OverlayConfig overlay,
        Snapshot snapshot,
        IReadOnlyList<FrameRate>? rates,
        int? front,
        bool windowOpen = false)
    {
        var withSlots = rates is null ? snapshot : snapshot with { Sensors = [.. snapshot.Sensors, .. FrameSlots.Of(rates)] };
        var read = GameSensors.With(withSlots, front);
        return OverlayLayout.Build(overlay, SensorMap.Read(read, overlay), English, windowOpen: windowOpen)
            .ToDictionary(item => item.Id, item => item.Value);
    }
}
