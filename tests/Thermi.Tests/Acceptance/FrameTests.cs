using System.Globalization;
using Thermi.Core.Editing;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Frames;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>From the presents the service traces to the FPS and frame time the overlay shows for the app in front.</summary>
public sealed partial class FrameTests
{
    private const int Game = 4242;

    private const string FpsSensor = "/game/0/framerate/0";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    // Zwift, an OpenGL game whose frames only the kernel's detailed history tells of, each repeated by a blit that is never
    // counted, beside a terminal that presents from a child of its own and an agent that presents through Direct3D 9. FPS
    // follows the game, not the focus: Zwift, maximized in front, counts as presenting from the third second and, with no
    // game before it, is the game from then; it stays it while the terminal, on another monitor, takes the focus,
    // maximized or not, at 18 FPS, and while the desktop is in front. The terminal alone, not filling its monitor, never
    // shows FPS.
    [Fact]
    public void TheOverlayShowsTheFramesOfTheAppInFront()
    {
        const int Zwift = 63632;
        const int Terminal = 27540;
        var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, FrameTimeItem] };
        (int? Front, nint? Fills)[] fronts =
            [(Zwift, 1), (Zwift, 1), (Zwift, 1), (Zwift, 1), (Zwift, 1), (Terminal, null), (Terminal, 2), (null, null)];
        var (game, terminal) = (new FrontGame(), new FrontGame());

        var seconds = Replay(TestFiles.Presents(TestRecordings.ZwiftFrames)).Zip(fronts).Select(each => string.Join(
            " | ",
            new[]
            {
                game.Next(each.First, each.Second.Front, each.Second.Fills, rate => rate.Process == Zwift ? [new(1, true, true)] : []),
                terminal.Next(each.First, Terminal, null, _ => []),
            }
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

    // Zwift in its menus, as OBS captured it, which the kernel tells of only by copying each frame to its window: maximized in front,
    // it counts as presenting from the third second and, with no game before it, is the game from then.
    [Fact]
    public void AGameThatObsCapturesShowsItsFrames()
    {
        const int Zwift = 16924;
        var game = new FrontGame();

        var shown = Replay(TestFiles.Presents(TestRecordings.ZwiftObsFrames)).Take(5)
            .Select(rates => Fps(OverlayConfig.First, Desktop, rates, game.Next(rates, Zwift, 1, _ => [new(1, true, true)])));

        Assert.Equal([null, null, "67", "162", "111"], shown);
    }

    // Zwift tells of its frames by its detailed history while riding and by BlitRect in its menus: riding for 4 s, then
    // in the menus for 5, then riding again, every second from the third reads its frames, never 0, and no wait it never had.
    [Fact]
    public void AGameThatChangesHowItPresentsKeepsItsFrames()
    {
        const int Zwift = 63632;
        var riding = TestFiles.Presents(TestRecordings.ZwiftFrames).Where(present => present.Process == Zwift).ToList();
        var menus = TestFiles.Presents(TestRecordings.ZwiftObsFrames).Select(present => present with { Process = Zwift, At = present.At + 4000 });
        var presents = riding.Where(present => present.At <= 4000).Concat(menus)
            .Concat(riding.Where(present => present.At > 4000).Select(present => present with { At = present.At + 5000 })).ToList();

        var zwift = Replay(presents).Take(13).Skip(2).Select(rates => rates.Single(rate => rate.Process == Zwift)).ToList();

        Assert.All(zwift, rate => Assert.True(rate.Fps >= 60 && rate.FrameTime < 100, $"{rate.Fps} FPS, {rate.FrameTime} ms"));
    }

    // A service that could not start its trace, one from before, which sends no slots, and the user's own host of a
    // per-user install, which has no rights to trace: the overlay keeps no room for FPS, while the window, open, shows it
    // as missing so it can be selected, and its catalog says why, frames that cannot be read or the install for everyone
    // they need, unlike while frames are read and nothing in front presents.
    [Fact]
    public void FramesThatCannotBeReadShowAsMissing()
    {
        (Snapshot Snapshot, NoValue Why)[] cases =
        [
            (Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of(null)] }, NoValue.FramesUnread),
            (Desktop, NoValue.FramesUnread),
            (Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.WithoutRights()] }, NoValue.FramesNeedInstallForEveryone),
        ];

        foreach (var (snapshot, why) in cases)
        {
            Assert.Null(Fps(OverlayConfig.First, snapshot, null, Game));
            Assert.Equal(ReadingFormatter.Missing, Fps(OverlayConfig.First, snapshot, null, Game, windowOpen: true));
            Assert.Equal(why, WhyFps(GameSensors.With(snapshot, Game)));
        }

        Assert.Equal(NoValue.NoFrames, WhyFps(GameSensors.With(Desktop with { Sensors = [.. Desktop.Sensors, .. FrameSlots.Of([])] }, Game)));

        // Linux reads no frames, so its readers add no game sensors: there the overlay keeps no room for them even while the
        // window is open, so an overlay beside GNOME's clock never grows over it.
        foreach (var (snapshot, shows) in new[] { (GameSensors.With(Desktop, null), true), (TestRecordings.Series(TestRecordings.Linux)[0], false) })
        {
            var input = new TrayInput(snapshot, ServiceState.Running, English, Texts.English, LightTaskbar: false, "") { WindowOpen = true };
            var scene = ThermiMark.Overlay(new ThermiConfig { Overlays = [OverlayConfig.First] }, 0, input, windowOpenedOnce: true);
            Assert.Equal(shows, scene.Items.Any(item => item.Id == Metrics.Fps.Id));
        }
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

    // FPS follows the game, second by second: with no game, a game that fills its monitor in front is one at once; a terminal
    // that fills the other monitor never takes it from a game shown on its own, at 35 FPS while the game presents at 65, nor
    // at its monitor's 180 while the game presents at 75; another game that fills the game's monitor takes over once it
    // presents faster, and the old one, in front again but slower, never takes it back; once the game stops presenting, or
    // fills no monitor, as minimized, the one in front is the game at once. With no game, as when thermi starts with a game
    // running behind, the fastest app that fills its monitor and presents at 30 or more is the game, in front or not, one
    // that presents from its own window before a browser's or a terminal's, which present from a child, and a video in a
    // window that does not fill its monitor never is.
    [Fact]
    public void FpsFollowsTheGameNotTheFocus()
    {
        const int Zwift = 1;
        const int Wave = 2;
        const int Other = 3;
        const int Video = 4;
        (int Process, double Fps)[] behind = [(Zwift, 160), (Wave, 25), (Video, 60)];
        var started = new FrontGame();
        Assert.Equal<int?>(
            [Zwift, Zwift, Zwift, Zwift],
            [.. Enumerable.Range(0, 4).Select(_ => started.Next(Rates(behind), Wave, 2, Windows))]);

        (int Process, double Fps)[] zwiftInFront = [(Zwift, 160), (Wave, 20)];
        (int Process, double Fps)[] waveBusy = [(Zwift, 65), (Wave, 35)];
        (int Process, double Fps)[] waveStreaming = [(Zwift, 75), (Wave, 180)];
        (int Process, double Fps)[] twoGames = [(Zwift, 65), (Other, 90)];
        ((int Process, double Fps)[] Rates, int? Front)[] seconds =
        [
            (zwiftInFront, Zwift), (zwiftInFront, Zwift), (zwiftInFront, Zwift),
            (waveBusy, Wave), (waveBusy, Wave), (waveBusy, Wave), (waveBusy, Wave),
            (waveStreaming, Wave), (waveStreaming, Wave), (waveStreaming, Wave), (waveStreaming, Wave),
            (twoGames, Other), (twoGames, Other), (twoGames, Other),
            (twoGames, Zwift), (twoGames, Zwift), (twoGames, Zwift),
            ([(Zwift, 65)], Zwift), ([(Zwift, 65)], Zwift), ([(Zwift, 65)], Zwift),
        ];
        var game = new FrontGame();

        var shown = seconds.Select(second => game.Next(Rates(second.Rates), second.Front, second.Front == Wave ? 2 : 1, Windows));

        Assert.Equal<int?>(
            [
                Zwift, Zwift, Zwift, Zwift, Zwift, Zwift, Zwift, Zwift, Zwift, Zwift, Zwift,
                Zwift, Zwift, Other, Other, Other, Other, Zwift, Zwift, Zwift,
            ],
            shown);

        var minimized = new FrontGame();
        _ = minimized.Next(Rates(zwiftInFront), Zwift, 1, Windows);
        Assert.Equal<int?>(Wave, minimized.Next(Rates(waveStreaming), Wave, 2, rate => rate.Process == Wave ? Windows(rate) : []));

        // As seen live: Zwift maximized at 98 on the left monitor, at negative X, while thermi starts with Chrome full screen on
        // the right one at 60, then in its left half playing a video at 24 under Wave in its right half; or with Wave in front
        // maximized at 180, then in its right half. Chrome and Wave present from a child, Zwift itself.
        FrameRate zwift = new(37756, 23716, 98, 10);
        (FrameRate[] Rates, int Front, bool Full) halves = ([zwift, new(4344, 35180, 24, 42), new(13848, 21308, 20, 50)], 21308, false);
        (FrameRate[] Rates, int Front, bool Full) waveFull = ([zwift, new(13848, 21308, 180, 6)], 21308, true);
        (FrameRate[] Rates, int Front, bool Full)[] netflix = [([zwift, new(4344, 35180, 60, 16)], 35180, true), halves, halves, halves];
        (FrameRate[] Rates, int Front, bool Full)[] wave = [waveFull, waveFull, waveFull, halves];
        foreach (var history in new[] { netflix, wave })
        {
            var live = new FrontGame();
            Assert.All(history, second =>
                Assert.Equal<int?>(37756, live.Next(second.Rates, second.Front, second.Full ? 0x1006d : null, rate => Live(rate, second.Full))));
        }

        static AppWindow[] Live(FrameRate rate, bool full)
        {
            return rate.Process == 37756 ? [new(0x20001, true, true)] : [new(0x1006d, full, false)];
        }

        static FrameRate[] Rates((int Process, double Fps)[] rates)
        {
            return [.. rates.Select(rate => new FrameRate(rate.Process, 0, rate.Fps, 10))];
        }

        // The terminal and a video on the second monitor, the rest filling the first.
        static AppWindow[] Windows(FrameRate rate)
        {
            return rate.Process switch { Wave => [new(2, true, false)], Video => [new(2, false, false)], _ => [new(1, true, true)] };
        }
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
