using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Settings;
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
    // from the window; ct0, ctall and clicks: click-through on for overlay 0, for all, and off for all.
    [Theory]
    [InlineData(2, "first=FirstRun:None open=~ flag=OverlaysHidden:ShowThem+GotIt show=~ flag=~ show=~")]
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
            var shown = notices.Update(new NoticeInput(Config(desktop.Hidden, desktop.Through), desktop.Flag, desktop.FromWindow, FirstStart: what == "first"));
            var now = shown is not null ? $"{shown.Kind}:{shown.Buttons.ToString().Replace(", ", "+", StringComparison.Ordinal)}" : notices.Showing is null ? "~" : ".";
            Assert.Equal((step, expected), (step, now));
            desktop.FromWindow = false;
        }
    }

    // Seconds of an item with Notify me: c critical at 95 °C, p critical at its peak of 98 °C, n normal at 60 °C, and the
    // capitals while a full-screen app is in front. A notice is the second it came, then w, its peak and its length in
    // seconds when it waited out the full-screen app.
    [Theory]
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
                told.Add(notice.WhilePlaying ? string.Create(CultureInfo.InvariantCulture, $"{second}w{notice.Value}/{notice.Length.TotalSeconds}") : second.ToString(CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(expected, string.Join(',', told));
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
