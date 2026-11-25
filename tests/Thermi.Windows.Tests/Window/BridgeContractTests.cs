using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Ui;
using Thermi.Ui.Editor;
using Thermi.Ui.Web;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// What the real bridge gives the page, over the recorded PC, held to what the page renders and the Playwright fake gives
/// it: every item and overlay named, every value with its level, each meter's chart in its own unit, the catalog, the
/// finder, the status and About filled, and the item it opened on: the views, which the fake shows in rough form. What
/// an edit, an undo or a look does the fake never decides; <see cref="BridgeOracleTests"/> holds its answers to C#'s.
/// </summary>
public sealed class BridgeContractTests
{
    // A version 2 file without names, as written by hand or by a build before names, its fan metered by its duty.
    private const string Unnamed = """
        {
          "version": 2,
          "overlays": [ {
            "name": "Floating 2",
            "snap": { "spot": "taskbar-right" },
            "items": [
              { "id": "fan.cpu", "metric": "fan.cpu", "meter": { "metric": "fan.cpu.duty" }, "threshold": { "warning": 60, "critical": 85 } },
              { "id": "cpu.temperature", "metric": "cpu.temperature", "meter": { "metric": "cpu.temperature" } },
              { "id": "cpu.power", "metric": "cpu.power" },
              { "id": "ram.used", "metric": "ram.used", "meter": { "metric": "ram.load" } }
            ]
          } ]
        }
        """;

    private static readonly Snapshot Recorded = TestRecordings.Read(TestRecordings.Admin);

    [Theory]
    [InlineData("defaults", "RPM", false)]
    [InlineData(Unnamed, "%", true)]
    public async Task TheRealBridgeFillsEveryFieldThePageRenders(string file, string fanUnit, bool fanTitled)
    {
        using var folder = new SettingsFolder();
        folder.Write(file == "defaults" ? ThermiConfigJson.Write(new ThermiConfig()) : file);
        await using var settings = folder.Settings();
        Assert.True(folder.Settled(settings.SyncAsync).IsCompletedSuccessfully);
        var turn = new Queue<Action>();
        var editor = new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock);
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        var environment = new WindowEnvironment(
            "thermi-tests",
            folder.File,
            "thermi.exe",
            folder.Log,
            folder.Clock.GetUtcNow(),
            new TestFeed { Latest = Recorded },
            startup)
        { Screens = () => [] };
        using var bridge = new WindowBridge(
            new WindowStart(1, settings, Texts.English, environment, folder.Clock) { Item = Metrics.CpuTemperature.Id },
            editor,
            new ChartHistory(folder.Clock),
            turn.Enqueue);
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
        folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        await using var settings = folder.Settings();
        Assert.True(folder.Settled(settings.SyncAsync).IsCompletedSuccessfully);
        var turn = new Queue<Action>();
        var editor = new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock);
        var feed = new TestFeed();
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        var environment = new WindowEnvironment(
            "thermi-tests",
            folder.File,
            "thermi.exe",
            folder.Log,
            folder.Clock.GetUtcNow(),
            feed,
            startup)
        { Screens = () => [], HotkeyStatus = () => null };
        using var bridge = new WindowBridge(
            new WindowStart(0, settings, Texts.English, environment, folder.Clock),
            editor,
            new ChartHistory(folder.Clock),
            turn.Enqueue);
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
        folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        await using var settings = folder.Settings();
        Assert.True(folder.Settled(settings.SyncAsync).IsCompletedSuccessfully);
        // The overlay's hotkeys are asked off the window's thread, which then queues the answer.
        var turn = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var editor = new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock);
        var feed = new TestFeed { Latest = Recorded };
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        var environment = new WindowEnvironment("thermi-tests", folder.File, "thermi.exe", folder.Log, folder.Clock.GetUtcNow(), feed, startup)
        {
            Screens = () => [],
            HotkeyStatus = () => null,
        };
        using var bridge = new WindowBridge(
            new WindowStart(0, settings, Texts.English, environment, folder.Clock),
            editor,
            new ChartHistory(folder.Clock),
            turn.Enqueue);
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
}
