using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The Windows notices from thermi's icon are state machines in Core: each shows once in its form and goes when what it is about ends.
/// </summary>
public sealed class NoticeTests
{
    private const string ItemId = "cpu.temperature";

    // Events, each then = and what follows it: a new notice as its kind and buttons, . for none new while one still shows,
    // ~ for none showing. first: thermi's first start; open: the window opens; flag and show: "Hide overlays" set and
    // cleared from outside the window; hide1 and show1: overlay 1 hidden on its own and shown; win: the next change comes
    // from the window; ct0, ctall and clicks: click-through on for overlay 0, for all, and off for all. first? is the first
    // start before the sensors are read, and first! one where the CPU reads zero, the driver missing (cold review 4, G7).
    [Theory] // Rows: each notice kind, the driver's first start, and a change from the window, which shows none
    [InlineData(2, "first=FirstRun:None open=~ flag=OverlaysHidden:ShowThem+GotIt show=~ flag=~ show=~")]
    [InlineData(1, "first?=~ first!=FirstRunDriver:None first!=. open=~")]
    [InlineData(1, "flag=OverlaysHidden:ShowIt+GotIt show=~")]
    [InlineData(2, "hide1=OverlayHidden:ShowIt+GotIt show1=~ hide1=~ show1=~")]
    [InlineData(2, "win hide1=~ show1=~ win flag=~ show=~")]
    [InlineData(2, "ct0=ClickThrough:Undo+GotIt clicks=~ ct0=ClickThrough:Undo+GotIt ctall=ClickThroughAll:Undo+GotIt clicks=~")]
    [InlineData(1, "first=FirstRun:None ct0=ClickThrough:Undo+GotIt clicks=~")]
    public void NoticesShowOnceAndGoWhenDone(int overlays, string steps)
    {
        var notices = new Notices();
        var desktop = new Desktop(overlays);

        foreach (var step in steps.Split(' '))
        {
            if (step == "win")
            {
                desktop.FromWindow = true;
                continue;
            }

            var (what, expected) = (step[..step.IndexOf('=', StringComparison.Ordinal)], step[(step.IndexOf('=', StringComparison.Ordinal) + 1)..]);
            desktop.Apply(what);
            var config = Config(desktop.Hidden, desktop.Through);
            var shown = notices.Update(
                new NoticeInput(config, desktop.Flag, desktop.FromWindow, FirstStart: what.StartsWith("first", StringComparison.Ordinal))
                {
                    DriverMissing = what == "first?" ? null : what == "first!",
                });
            // Every OS's notice says it with each blank filled, the toggle hotkey's too.
            foreach (var hotkeys in shown is null ? [] : new HotkeysConfig?[] { null, new() { Toggle = "Ctrl+Alt+H" } })
            {
                var (title, text) = NoticeTexts.Of(shown!, config with { Hotkeys = hotkeys }, Texts.English, CultureInfo.InvariantCulture);
                Assert.DoesNotContain("{", title + text, StringComparison.Ordinal);
            }

            var now = shown is not null
                ? $"{shown.Kind}:{shown.Buttons.ToString().Replace(", ", "+", StringComparison.Ordinal)}"
                : notices.Showing is null ? "~" : ".";
            Assert.Equal((step, expected), (step, now));
            desktop.FromWindow = false;
        }
    }

    // Seconds of an item with Notify me: c critical at 95 °C, p critical at its peak of 98 °C, n normal at 60 °C, and the
    // capitals while a full-screen app is in front. A notice is the second it came, then w, its peak and its length in
    // seconds when it waited out the full-screen app.
    [Theory] // Rows: 9 and 10 s straddle the wait, then staying, coming back, and full screen with and without a peak, each A192 states
    [InlineData("cccccccccc", "")]
    [InlineData("ccccccccccc", "11")]
    [InlineData("cccccccccccccccccccccccccccccccccccccccc", "11")]
    [InlineData("ccccccccccccnnncccccccccccc", "11,26")]
    [InlineData("CCCCCCCCCCPCCCCCCCCCn", "21w98/20")]
    [InlineData("CCCCCCCCCCCCNNNn", "16w95/12")]
    public void NotifyMeTellsOnceAnEpisode(string seconds, string expected)
    {
        var time = new FakeTimeProvider();
        var notify = new NotifyMe(time);
        var told = new List<string>();

        for (var second = 1; second <= seconds.Length; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            var at = seconds[second - 1];
            var (level, value) = char.ToLowerInvariant(at) switch { 'p' => (Level.Critical, 98.0), 'c' => (Level.Critical, 95.0), _ => (Level.Normal, 60.0) };
            if (notify.Add(ItemId, level, value, fullScreen: char.IsUpper(at)) is { } notice)
            {
                Assert.Equal(ItemId, notice.ItemId);
                told.Add(notice.WhilePlaying
                    ? string.Create(CultureInfo.InvariantCulture, $"{second}w{notice.Value}/{notice.Length.TotalSeconds}")
                    : second.ToString(CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(expected, string.Join(',', told));
    }

    // From a recorded desktop: the CPU past its own critical line is told at its eleventh second, with its reading; an item
    // that does not ask is not, nor one whose sensor the desktop lacks, a battery.
    [Fact]
    public void AnItemThatAsksIsToldFromTheSnapshot()
    {
        var time = new FakeTimeProvider();
        var notify = new NotifyMe(time);
        var hot = new ItemConfig
        {
            Id = ItemId,
            Metric = Metrics.CpuTemperature.Id,
            Threshold = new Threshold(critical: 1),
            Notify = true,
        };
        Metric[] others = [Metrics.CpuLoad, Metrics.BatteryCharge];
        var items = others.Select(metric => hot with { Id = metric.Id, Metric = metric.Id });
        var config = new ThermiConfig { Overlays = [new OverlayConfig { Items = [hot, .. items.Select((item, at) => item with { Notify = at == 1 })] }] };
        var snapshot = TestRecordings.Read(TestRecordings.Admin);
        var told = new List<ItemNotice>();

        for (var second = 1; second <= 11; second++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            told.AddRange(notify.Add(config, snapshot, fullScreen: false));
        }

        var notice = Assert.Single(told);
        Assert.Equal((ItemId, SensorMap.Read(snapshot, config.Overlays[0])[ItemId].Value), (notice.Item.Id, (double?)notice.Notice.Value));
    }

    /// <summary>What the desktop holds between the steps: overlays hidden on their own, click-through, and "Hide overlays".</summary>
    private sealed class Desktop(int overlays)
    {
        public bool[] Hidden { get; } = new bool[overlays];

        public bool[] Through { get; } = new bool[overlays];

        public bool Flag { get; private set; }

        public bool FromWindow { get; set; }

        public void Apply(string what)
        {
            switch (what)
            {
                case "flag" or "show":
                    Flag = what == "flag";
                    break;
                case "hide1" or "show1":
                    Hidden[^1] = what == "hide1";
                    break;
                case "ct0":
                    Through[0] = true;
                    break;
                case "ctall" or "clicks":
                    Array.Fill(Through, what == "ctall");
                    break;
                default:
                    break;
            }
        }
    }

    private static ThermiConfig Config(bool[] hidden, bool[] through)
    {
        var overlays = hidden.Select((each, at) => $"{{'hidden':{(each ? "true" : "false")},'clickThrough':{(through[at] ? "true" : "false")}}}");
        var (config, problem) = ThermiConfigJson.Read($"{{'version':2,'overlays':[{string.Join(',', overlays)}]}}".Replace('\'', '"'));
        Assert.Null(problem);
        return config;
    }
}
