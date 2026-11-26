using System.Globalization;
using Thermi.App.SettingsFile;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Ui;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Ui.Web;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// "Find it" through the window's bridge, without ever loading the CPU: a load of the test's own records when it would
/// spin and when it stops, on the fake clock, over the recorded snapshot with the board's fan answering or not.
/// </summary>
public sealed class FinderTests : IAsyncDisposable
{
    // The fan the board reports first, which cools the CPU on the author's PC.
    private const string BoardFan = "/lpc/it8689e/0/fan/0";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly Snapshot Recorded = TestRecordings.Read(TestRecordings.Admin);

    private readonly SettingsFolder _folder = new();
    private readonly TestFeed _feed = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _turn = new();
    private readonly List<CancellationToken> _loads = [];
    private readonly Settings _settings;
    private readonly WindowEditor _editor;
    private readonly WindowBridge _bridge;

    public FinderTests()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        _settings = _folder.Settings();
        Assert.True(_folder.Settled(_settings.SyncAsync).IsCompletedSuccessfully);
        _editor = new WindowEditor(_settings, _turn.Enqueue, _folder.Log, _folder.Clock);
        var start = new WindowStart(1, _settings, Texts.English, _folder.Window(_feed), _folder.Clock) { Load = _loads.Add };
        _bridge = new WindowBridge(start, _editor, new ChartHistory(_folder.Clock), _turn.Enqueue);
    }

    public async ValueTask DisposeAsync()
    {
        _bridge.Dispose();
        await _settings.DisposeAsync();
        _folder.Dispose();
    }

    // R19, R21: 3 s of readings, then the load, which stops as the board's fan meets the rule past 8 s; the pick is the
    // board's fan, and nothing changes until "Use it" makes the item show it.
    [Fact]
    public async Task FindItLoadsTheCoresUntilAFanAnswersAndChangesNothing()
    {
        var before = ThermiConfigJson.Write(_editor.Config);
        _feed.Latest = Second(0, answers: true);
        await _bridge.FindCpuFan();
        var waiting = await _bridge.Finder();

        Feed(seconds: 3, answers: true);
        var loading = (_loads.Count, _loads[0].IsCancellationRequested);
        Feed(seconds: 2, answers: true, from: 4);
        var rising = await _bridge.Finder();
        Feed(seconds: 6, answers: true, from: 6);
        var done = await _bridge.Finder();

        Assert.Equal(new FinderView(FinderPhase.Running, null, 0, Texts.English.Of(CatalogText.FanFinderWaiting)), waiting);
        Assert.Equal((1, false), loading);
        Assert.Equal(FinderPhase.Running, rising.Phase);
        Assert.Equal(
            $"{Texts.English.Of(CatalogText.FanFinderLoading)} · "
            + Texts.English.Format(English, CatalogText.FanRising, "CPU fan · ITE IT8689E", 2174, 200),
            rising.Text);
        Assert.Equal(2.0 / 30, rising.Progress!.Value, 3);
        Assert.Equal(
            (FinderPhase.Done, BoardFan, Texts.English.Format(English, FanFinderOutcome.Found, "CPU fan · ITE IT8689E")),
            (done.Phase, done.Pick, done.Text));
        Assert.True(_loads[0].IsCancellationRequested);
        Assert.Equal(before, ThermiConfigJson.Write(_editor.Config));
        Assert.False(_editor.CanUndo);
    }

    // R20: a cancel stops the load at once, and nothing changes; a second puts the run away.
    [Fact]
    public async Task CancellingStopsTheLoadAtOnceAndChangesNothing()
    {
        var before = ThermiConfigJson.Write(_editor.Config);
        _feed.Latest = Second(0, answers: true);
        await _bridge.FindCpuFan();
        Feed(seconds: 5, answers: true);

        await _bridge.CancelFind();
        var cancelled = await _bridge.Finder();
        await _bridge.CancelFind();

        Assert.True(_loads.Single().IsCancellationRequested);
        Assert.Equal(FinderPhase.Cancelled, cancelled.Phase);
        Assert.Equal(new FinderView(FinderPhase.Idle, null), await _bridge.Finder());
        Assert.False(_editor.CanUndo);
        Assert.Equal(before, ThermiConfigJson.Write(_editor.Config));
    }

    // R19, R22: no fan answers, so the load ends at 30 s, and the finder says why once it has watched the fans for 40 s after it;
    // the readings stop, and so does the load, and they never come back.
    [Theory]
    [InlineData(false, 70, FanFinderOutcome.NoFanRose)]
    [InlineData(true, 0, FanFinderOutcome.ReadingsStopped)]
    public async Task WhenNoFanAnswersOrTheReadingsStopTheLoadEndsAndTheFinderSaysWhy(bool answers, int more, FanFinderOutcome outcome)
    {
        var stopped = TimeSpan.Zero;
        _feed.Latest = Second(0, answers);
        await _bridge.FindCpuFan();
        Feed(seconds: 3, answers);
        var loadStart = _folder.Clock.GetTimestamp();
        _loads.Single().Register(() => stopped = _folder.Clock.GetElapsedTime(loadStart));

        Feed(seconds: more, answers, from: 4);
        Tick(FanFinder.MaxGap + TimeSpan.FromSeconds(0.5));
        Tick(FanFinder.MaxGap + TimeSpan.FromSeconds(0.5));
        var done = await _bridge.Finder();

        Assert.True(_loads.Single().IsCancellationRequested);
        Assert.Equal(new FinderView(FinderPhase.Done, null, Text: Texts.English.Of(outcome)), done);
        Assert.True(more == 0 || stopped == TimeSpan.FromSeconds(30), $"The load ran {stopped}.");
        Assert.False(_editor.CanUndo);
    }

    // R22: without a fan outside the GPUs it cannot start, and says whether the driver may be missing; without readings, neither.
    [Fact]
    public async Task WithoutBoardFansFindItCannotStartAndSaysWhy()
    {
        var noBoardFans = new Snapshot([.. Recorded.Sensors.Where(sensor => !(sensor.HardwareType == "SuperIO" && sensor.Type == "Fan"))]);
        var noDriver = new Snapshot(
        [
            .. noBoardFans.Sensors.Select(sensor =>
                sensor.HardwareType == "Cpu" && sensor.Type == "Temperature" ? sensor with { Value = 0 } : sensor),
        ]);
        var refusals = new List<FanFinderRefusal?>();

        foreach (var snapshot in new[] { noBoardFans, noDriver, null, Recorded })
        {
            _feed.Latest = snapshot;
            refusals.Add((await _bridge.Finder()).Refusal);
        }

        _feed.Latest = noDriver;
        await _bridge.FindCpuFan();

        Assert.Equal([FanFinderRefusal.NoBoardFans, FanFinderRefusal.DriverMayBeMissing, null, null], refusals);
        Assert.Equal(FinderPhase.Idle, (await _bridge.Finder()).Phase);
        Assert.Empty(_loads);
    }

    // The recorded snapshot a given second into a run: from the fourth on, the CPU at full load, warmer and drawing more,
    // as a load makes it, and, when it answers, the board's fan three times as fast, so the finder picks it.
    private static Snapshot Second(int second, bool answers) =>
        new([.. Recorded.Sensors.Select(sensor => sensor.Id switch
        {
            BoardFan when second > 3 && answers => sensor with { Value = sensor.Value * 3 },
            "/amdcpu/0/load/0" when second > 3 => sensor with { Value = 100 },
            "/amdcpu/0/temperature/2" when second > 3 => sensor with { Value = sensor.Value + 15 },
            "/amdcpu/0/power/0" when second > 3 => sensor with { Value = sensor.Value * 2 },
            _ => sensor,
        })]);

    // A fresh snapshot a second, as the pipe gives them, the clock's ticks in between, taken on the window's turn.
    private void Feed(int seconds, bool answers, int from = 1)
    {
        for (var second = from; second < from + seconds; second++)
        {
            Tick(TimeSpan.FromSeconds(1));
            _feed.Latest = Second(second, answers);
            _bridge.Arrived();
        }
    }

    private void Tick(TimeSpan span)
    {
        _folder.Clock.Advance(span);
        while (_turn.TryDequeue(out var action))
        {
            action();
        }
    }
}
