using System.Collections.Concurrent;
using System.Globalization;
using Thermi.App.Window;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Ui;
using Thermi.Windows.Ui.Web;
using History = Thermi.Core.Messages.History;
using WindowMessages = Thermi.Core.Messages.WindowMessages;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// What the real bridge gives the page, over the recorded PC, held to what the page renders and the Playwright fake gives
/// it: every item and overlay named, every value with its level, each meter's chart in its own unit, the catalog, the
/// finder, the status and About filled, and the item it opened on: the views, which the fake shows in rough form. What
/// an edit, an undo or a look does the fake never decides; <see cref="BridgeOracleTests"/> holds its answers to C#'s.
/// </summary>
public sealed class BridgeContractTests
{
    // A version 2 file without names, as written by hand or by a build before names, its fan metered by its duty, and its CPU
    // temperature, its fan and its RAM in two decimals.
    private const string Unnamed = """
        {
          "version": 2,
          "overlays": [ {
            "name": "Floating 2",
            "snap": { "spot": "taskbar-right" },
            "items": [
              { "id": "fan.cpu", "metric": "fan.cpu", "meter": { "metric": "fan.cpu.duty" }, "threshold": { "warning": 60, "critical": 85 }, "decimals": 2 },
              { "id": "cpu.temperature", "metric": "cpu.temperature", "meter": { "metric": "cpu.temperature" }, "decimals": 2 },
              { "id": "cpu.power", "metric": "cpu.power" },
              { "id": "ram.used", "metric": "ram.used", "meter": { "metric": "ram.load" }, "decimals": 2 }
            ]
          } ]
        }
        """;

    private static readonly Snapshot Recorded = TestRecordings.Read(TestRecordings.Admin);

    // A chart of the item's own reading says its last point as the item's value says it, in its decimals too; one of another
    // reading, a fan's duty or the RAM's load, in that reading's own.
    private static string? Said(ChartView chart, ItemView item) =>
        chart.Title is null ? item.Value
        : ReadingFormatter.Format(
            chart.Title == Texts.English.Of(ChartText.DutyTitle) ? SensorKind.Control : SensorKind.Load,
            chart.Readings[^1] ?? double.NaN,
            new ValueFormat(Texts.English, CultureInfo.CurrentCulture));

    [Theory]
    [InlineData("defaults", "RPM", false)]
    [InlineData(Unnamed, "%", true)]
    public async Task TheRealBridgeFillsEveryFieldThePageRenders(string file, string fanUnit, bool fanTitled)
    {
        using var folder = new SettingsFolder();
        folder.Write(file == "defaults" ? ThermiConfigJson.Write(new ThermiConfig()) : file);
        await using var settings = folder.Settings();
        Assert.True(folder.Settled(settings.SyncAsync).IsCompletedSuccessfully);
        var turn = new ConcurrentQueue<Action>();
        var window = folder.Window(new TestFeed { Latest = Recorded });
        var start = new WindowStart(1, settings, Texts.English, window, folder.Clock) { Item = Metrics.CpuTemperature.Id };
        var editor = new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock);
        using var bridge = new WindowBridge(start, editor, new ChartHistory(folder.Clock), turn.Enqueue);
        for (var second = 0; second < 3; second++)
        {
            folder.Clock.Advance(TimeSpan.FromSeconds(1));
            bridge.Arrived();
        }

        var config = await bridge.Settings();
        var view = await bridge.View();
        var items = config.Overlays.SelectMany(overlay => overlay.Items).ToList();

        Assert.All(config.Overlays, overlay => Assert.False(string.IsNullOrWhiteSpace(overlay.Name)));
        Assert.All(items, item => Assert.Equal(Texts.English.Of(Metrics.Find(item.Metric ?? item.Auto)!.Name), item.Name));
        Assert.Equal(
            config.Overlays.Select(overlay => overlay.Items.Select(item => item.Id)),
            view.Pills.Select(pills => pills.SelectMany(pill => pill.Items)));
        Assert.All(view.Pills.SelectMany(pills => pills), pill => Assert.False(string.IsNullOrWhiteSpace(pill.Label)));
        Assert.All(
            items,
            item => Assert.True(
                view.Items[item.Id] is { Value: not null, Type.Length: > 0 } shown
                    && Threshold.Judges(item) == shown.Judged
                    && shown.Judged != (shown.Level is null),
                item.Id));
        Assert.All(
            items,
            item => Assert.Equal(
                item.WarnBy?.Metric is { } metric ? Texts.English.Of(Metrics.Find(metric)!.Name) : null,
                view.Items[item.Id].WarnByName));
        // Every numeric item charts, one lines cannot judge its own reading with no lines.
        foreach (var item in items)
        {
            var chart = await bridge.Chart(item.Id);
            Assert.True(chart is { Empty: null, Stats: not null } && (Threshold.Judges(item) || chart is { Warning: null, Critical: null }), item.Id);
            var unit = chart.Stats.Split(' ')[^1];
            Assert.All(new[] { chart.WarningText, chart.CriticalText }.OfType<string>(), line => Assert.EndsWith($" {unit}", line, StringComparison.Ordinal));
            var now = Texts.English.Of(UiText.ChartNow);
            Assert.Equal(Texts.English.Format(CultureInfo.InvariantCulture, ChartText.Point, Said(chart, view.Items[item.Id]), now), chart.Points[^1]);
            if (item.Id == Metrics.CpuFan.Id)
            {
                Assert.Equal((fanUnit, fanTitled), (unit, chart.Title == Texts.English.Of(ChartText.DutyTitle)));
            }
        }

        var catalog = await bridge.Catalog("", [], all: true);
        Assert.True(catalog.Count > 0);
        Assert.All(catalog.Groups, group => Assert.False(string.IsNullOrWhiteSpace(group.Name)));
        Assert.All(
            catalog.Groups.SelectMany(group => group.Sensors),
            row => Assert.True(
                row is { Name.Length: > 0, Minute.Count: > 0 } && (row.Value ?? row.Why?.ToString()) is not null,
                row.Id));
        Assert.Equal(new FinderView(FinderPhase.Idle, null), await bridge.Finder());
        Assert.Null((await bridge.Status()).Problem);
        Assert.Equal(new UndoSteps(null, null), await bridge.Steps());
        // A look another program deleted first is refused, as a rename of it is, so the page can say so.
        Assert.Equal(Texts.Key(Core.Editing.Refusal.StyleNotFound), await bridge.DeleteLook("Gone"));
        // The version lives in Directory.Build.props alone, and About shows it as it stands there.
        var version = System.Xml.Linq.XDocument.Load(Path.Combine(TestRecordings.RepoRoot(), "Directory.Build.props")).Descendants("Version").Single().Value;
        Assert.True((await bridge.About()) is { Languages.Count: > 1, Windows.Length: > 0 } about && about.Version == version, version);
        // Opened from a double-click on a value, the page starts on that item of the first overlay.
        Assert.True((await bridge.Start()) is { Overlay: 0, Item: "cpu.temperature" });
    }

    // A window opened before the pipe's first snapshot shows nothing as missing that the overlay already shows: what the
    // page reads waits for it. After it, a second's readings tell the page to read only what readings change.
    [Fact]
    public async Task ThePageWaitsForTheFirstReadingsAndThenHearsOfEachSecondApart()
    {
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(new ThermiConfig());
        var feed = new TestFeed();
        using var bridge = folder.Bridge(settings, folder.Window(feed), new ConcurrentQueue<Action>());
        var heard = new List<string>();
        bridge.Changed += (_, _) => heard.Add(nameof(bridge.Changed));
        bridge.Readings += (_, readings) => heard.Add(readings.View.Items.Values.All(item => item.Value is not null) ? nameof(bridge.Readings) : "empty");

        var view = bridge.View();
        Assert.False(view.IsCompleted);
        feed.Latest = Recorded;
        bridge.Arrived();
        Assert.All((await view).Items.Values, item => Assert.NotNull(item.Value));

        heard.Clear();
        for (var second = 0; second < 3; second++)
        {
            folder.Clock.Advance(TimeSpan.FromSeconds(1));
            bridge.Arrived();
        }

        // The page reads the sensors only at a change, so the service's going stale is one.
        feed.State = ServiceState.Stale;
        bridge.Arrived();
        Assert.Equal([nameof(bridge.Readings), nameof(bridge.Readings), nameof(bridge.Readings), nameof(bridge.Changed)], heard);
    }

    // A service that stops sends nothing more, so no snapshot tells the window: within two seconds of the last one
    // the page hears of it anyway, to show the bar that starts it again instead of numbers that look live.
    [Fact]
    public async Task AServiceThatGoesSilentReachesThePageWithoutASnapshot()
    {
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(new ThermiConfig());
        // The overlay's hotkeys are asked off the window's thread, which then queues the answer.
        var turn = new ConcurrentQueue<Action>();
        var feed = new TestFeed { Latest = Recorded };
        using var bridge = folder.Bridge(settings, folder.Window(feed), turn);
        bridge.Arrived();
        var told = new List<bool>();
        bridge.Changed += (_, change) => told.Add(change.Sensors);

        (feed.Latest, feed.State) = (null, ServiceState.Stopped);
        for (var tick = 0; tick < 12; tick++)
        {
            folder.Clock.Advance(TimeSpan.FromMilliseconds(250));
            while (turn.TryDequeue(out var action))
            {
                action();
            }
        }

        Assert.Equal([true], told);
    }

    // A snapshot Core failed to name ended the window. One the window cannot read leaves it on the last it read, said once
    // in the log however many such seconds come, and the next it reads shows.
    [Fact]
    public async Task ASnapshotTheWindowCannotReadLeavesItsLastViewUntilOneReads()
    {
        var cpu = Metrics.CpuTemperature.Id;
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(new ThermiConfig());
        var feed = new TestFeed { Latest = Recorded };
        using var bridge = folder.Bridge(settings, folder.Window(feed), new ConcurrentQueue<Action>());
        bridge.Arrived();
        var last = (await bridge.View()).Items[cpu].Value;
        var heard = 0;
        bridge.Readings += (_, _) => heard++;
        bridge.Changed += (_, _) => heard++;

        for (var second = 0; second < 3; second++)
        {
            folder.Clock.Advance(TimeSpan.FromSeconds(1));
            feed.Latest = new Snapshot(new Unreadable());
            bridge.Arrived();
        }

        Assert.Equal((0, last), (heard, (await bridge.View()).Items[cpu].Value));
        Assert.Single(folder.Log.Entries, entry => entry.Error?.Message == Unreadable.Why);

        feed.Latest = HostRun.Warmer;
        bridge.Arrived();
        Assert.True(heard == 1 && (await bridge.View()).Items[cpu].Value != last, last);
    }

    // The CPU temperature set to CCD2, then undone back to Auto, kept CCD2's seconds in its chart, a dip to 49 °C: the
    // metric's own history had followed the sensor that stood in for it.
    [Fact]
    public async Task AChartUndoneBackToAutoShowsOnlyTheAutomaticPick()
    {
        const string ccd2 = "/amdcpu/0/temperature/4";
        var cpu = Metrics.CpuTemperature.Id;
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(new ThermiConfig());
        var turn = new ConcurrentQueue<Action>();
        var editor = new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock);
        var feed = new TestFeed();
        var start = new WindowStart(0, settings, Texts.English, folder.Window(feed), folder.Clock);
        using var bridge = new WindowBridge(start, editor, new ChartHistory(folder.Clock), turn.Enqueue);
        var cold = new Snapshot([.. Recorded.Sensors.Select(sensor => sensor.Id == ccd2 ? sensor with { Value = 34 } : sensor)]);
        void Feed(Snapshot snapshot, int seconds)
        {
            for (var second = 0; second < seconds; second++)
            {
                folder.Clock.Advance(TimeSpan.FromSeconds(1));
                feed.Latest = snapshot;
                bridge.Arrived();
            }
        }

        Feed(Recorded, 3);
        Assert.Null(editor.Edit("sensor", config => ItemEdits.ChangeSensor(config, cpu, Recorded, ccd2)).Refused);
        Feed(cold, 4);
        editor.Undo();
        Feed(Recorded, 1);

        Assert.DoesNotContain(34, (await bridge.History(cpu)).OfType<double>());
    }

    // A323: the owner's window said "RAM used 31.8 GB" while his overlay said "RAM 40.1 GB", its average, and the label box
    // offered "GPU", the bar's first label. The overlay runs a while before the window opens, the window takes its history
    // through the message, and from then on both read the recorded series: each second, the window's item shows the very
    // text, label and run the overlay draws. MessageTests holds the other stats, smoothing, offsets and resets to it.
    [Fact]
    public async Task TheWindowShowsEachItemAsTheOverlayDrawsIt()
    {
        var series = TestRecordings.Series(TestRecordings.CpuLoad);
        var cpu = OverlayConfig.DefaultItems[2] with { Stat = ItemStat.Average };
        var overlay = new OverlayConfig { Items = [OverlayConfig.DefaultItems[4], cpu], Style = new StyleConfig { Layout = PillLayout.One } };
        var config = new ThermiConfig { Overlays = [overlay] };
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(config);
        var feed = new TestFeed();
        var chart = new ChartHistory(folder.Clock);
        var onOverlay = new ItemHistory(folder.Clock);
        using var bridge = folder.Bridge(settings, folder.Window(feed), new ConcurrentQueue<Action>(), chart: chart);
        var drawn = new List<(string?, string?)>();
        var shown = new List<(string?, string?)>();

        for (var second = 0; second < series.Count; second++)
        {
            folder.Clock.Advance(TimeSpan.FromSeconds(1));
            var readings = SensorMap.Read(series[second], overlay);
            onOverlay.Add(overlay, readings);
            if (second == series.Count / 3)
            {
                chart.Take((History)WindowMessages.Decode(WindowMessages.Encode(onOverlay.Sent(config)))!);
            }

            if (second >= series.Count / 3)
            {
                feed.Latest = series[second];
                bridge.Arrived();
                var painted = OverlayLayout.Build(overlay, readings, CultureInfo.CurrentCulture, Texts.English, history: onOverlay);
                var item = (await bridge.View()).Items[cpu.Id];
                var own = painted.Single(each => each.Id == cpu.Id);
                drawn.Add((own.Value, own.Label));
                shown.Add((item.Value, item.Label));
                Assert.True(item.Tag is { Length: > 0 } tag && tag == own.Tag && !item.Label!.Contains(tag, StringComparison.Ordinal), item.Label);
            }
        }

        Assert.Equal(drawn, shown);
        Assert.NotEqual(OverlayLayout.LabelOf(OverlayConfig.DefaultItems[4], Texts.English), shown[0].Item2);
    }

    // The preview is painted as high as the overlay shows, in the monitor's pixels: at size 2 on a taskbar 48 px high at
    // 100 %, or 72 px at 150 %, no higher than the taskbar; free at 150 %, at its own size. In a column narrower than that
    // it is painted smaller to the column's width, never as wide as the overlay and then shrunk, which blurred it.
    [Theory]
    [InlineData("taskbar 100%", 0, 48)]
    [InlineData("taskbar 150%", 0, 72)]
    [InlineData("free 150%", 0, 114)]
    [InlineData("free 150%", 400, 0)]
    public async Task ThePreviewIsAsHighAsTheOverlayShows(string placed, int room, double high)
    {
        var words = placed.Split(' ');
        var scale = words[1] == "150%" ? 1.5 : 1;
        var bar = (int)(48 * scale);
        var screen = new Screen("PC", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1440 - bar), scale)
        {
            Taskbar = new PixelRect(0, 1440 - bar, 2560, 1440),
        };
        var overlay = new OverlayConfig(2) { Position = words[0] == "free" ? new Position(100, 100) : null };
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(new ThermiConfig { Overlays = [overlay] });
        var window = folder.Window(new TestFeed { Latest = Recorded }) with { Screens = () => [screen] };
        using var bridge = folder.Bridge(settings, window, new ConcurrentQueue<Action>());
        bridge.Arrived();

        var whole = await bridge.Preview(0, 0);
        var shown = await bridge.Preview(0, room);

        if (room == 0)
        {
            Assert.Equal(high, shown!.Height, 2);
        }
        else
        {
            // Text is measured again at the smaller scale, so its width lands within a few pixels of the room, never past it.
            Assert.True(whole!.Width > room && shown!.Width <= room && shown.Width >= room - 8, $"{whole.Width} to {shown.Width}");
            Assert.Equal(whole.Height * shown.Width / whole.Width, shown.Height, 1.0);
        }
    }

    // Sensors Core fails on wherever it reads them, as it failed on a name shared three ways, with the very error it threw.
    private sealed class Unreadable : IReadOnlyList<RawSensor>
    {
        public const string Why = "Nullable object must have a value.";

        public int Count => throw new InvalidOperationException(Why);

        public RawSensor this[int index] => throw new InvalidOperationException(Why);

        public IEnumerator<RawSensor> GetEnumerator() => throw new InvalidOperationException(Why);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
