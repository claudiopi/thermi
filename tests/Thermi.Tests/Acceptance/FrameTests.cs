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

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    [Fact(Skip = "Waiting for its recording")]
    public void TheOverlayShowsTheFramesOfTheAppInFront()
    {
        var presents = TestFiles.Presents(TestRecordings.ZwiftFrames);

        var shown = Replay(presents).Select(rates => Fps(OverlayConfig.First, Desktop, rates, Game)).ToList();

        Assert.NotEmpty(shown);
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
    }

    // Frames per second are in trouble when low, a frame time when high, in the value and in each second of its line, a
    // value at a line reaching it: 144 FPS is fine, 60 a warning and 30 critical; 7 ms fine, 50 ms a warning and 100 ms
    // critical; each on its own scale. A frame rate's critical above its warning is refused.
    [Fact]
    public void FrameRateWarnsLowAndFrameTimeHigh()
    {
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, FrameTimeItem] };
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

    // What the overlay shows for FPS and frame time each second, from the presents the service traced, through the slots
    // its snapshot carries. A second's FPS is its presents over its length; its frame time the longest wait for a frame that
    // ended in it, or the one still going; a process that stops presenting reads 0 FPS for 5 s, then takes no room; a
    // present the trace delivers after its second closed is not counted in the next; presents delivered out of order are
    // counted in order. The kernel's presents, marked k, count only for a process that sends no runtime's, whose presents
    // the kernel repeats, and its detailed ones, marked d, only for one that sends neither.
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
        var tally = new FrameTally();
        var sent = presents.Split(' ')
            .Select(each => (
                At: double.Parse(each.TrimStart('k', 'd'), CultureInfo.InvariantCulture),
                Source: each[0] switch { 'k' => PresentSource.Kernel, 'd' => PresentSource.KernelDetailed, _ => PresentSource.Dxgi }))
            .ToList();
        var times = sent.Select(each => each.At).ToList();
        var seconds = new List<string>();
        var delivered = 0;
        foreach (var close in closes.Split(' ').Select(each => double.Parse(each, CultureInfo.InvariantCulture)))
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
        new() { Id = Metrics.FrameTime.Id, Metric = Metrics.FrameTime.Id, Threshold = Threshold.Default(Metrics.FrameTime) };

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
