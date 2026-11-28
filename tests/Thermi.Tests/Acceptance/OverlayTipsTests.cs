using System.Drawing;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Gestures;
using Thermi.App.Overlay;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// An overlay's value tooltips where the desktop has none an overlay can use, as Windows' tooltip control shows them, and
/// the overlay's own clock: its critical glow's frames and OLED care's steps.
/// </summary>
public sealed class OverlayTipsTests
{
    private static readonly TimeSpan DoubleClick = TimeSpan.FromMilliseconds(500);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly FakeTimeProvider _time = new();

    // A value's tooltip shows once the pointer rests on it for the double-click time, the next one's a fifth of it after
    // while one shows, its text following the value; it goes after ten times the wait and comes back only for another
    // value, and as the pointer leaves. It shows whole above the overlay, or below it at the top of the screen.
    [Fact]
    public void AValuesTooltipShowsAsWindowsShowsIt()
    {
        var host = new Tips(_time);
        using var tips = new OverlayTips(host, DoubleClick);
        var (admin, noAdmin) = (Input(TestRecordings.Admin), Input(TestRecordings.NoAdmin));
        Update(tips, new ThermiConfig(), admin);

        tips.Point(150, 20);
        Pass(499);
        var early = host.Shown.Count;
        Pass(1);
        tips.Point(50, 20);
        Pass(100);
        Update(tips, new ThermiConfig(), noAdmin);
        Pass(5000);
        tips.Point(60, 20);
        Pass(600);
        tips.Point(50, 100);
        tips.Point(150, 20);
        Pass(500);
        tips.Left();

        Assert.Equal(0, early);
        Assert.Equal([Text(admin, 1), null, Text(admin, 0), Text(noAdmin, 0), null, Text(noAdmin, 1), null], host.Shown.Select(tip => tip?.Text));
        Assert.NotEqual(Text(admin, 0), Text(noAdmin, 0));
        Assert.Equal(new PillBox(0, 0, 100, 40), host.Shown[2]?.Item);
        Assert.Equal(1334, OverlayTips.Top(new PixelRect(1000, 1400, 1500, 1438), 60, 0));
        Assert.Equal(new Position(2260, 44), OverlayTips.Place(new PixelRect(1000, 0, 1500, 38), new Size(300, 60), 2550, new PixelRect(0, 0, 2560, 1440)));
    }

    // No tooltip shows while a press or the menu holds the overlay, nor after a drop until the pointer leaves the overlay
    // or 1.5 s pass, each drop once; once free, the value under the pointer shows its own after the usual wait. A press
    // or a menu forgets the value the pointer was on, as it may move unheard under them: the value it next moves on shows
    // its own, never the pressed one's (the final pass on GNOME 46, 2026-11-27). A click-through overlay shows none.
    [Fact]
    public void NoTooltipShowsWhileHeldAfterADropOrClickingThrough()
    {
        var host = new Tips(_time);
        using var tips = new OverlayTips(host, DoubleClick);
        var input = Input(TestRecordings.Admin);
        var drop = new DropQuiet(_time);
        Update(tips, new ThermiConfig(), input);

        tips.Point(50, 20);
        tips.Quiet(Hush.Press, true);
        Pass(600);
        tips.Quiet(Hush.Press, false);
        Pass(500);
        tips.Point(150, 20);
        Pass(500);
        tips.Quiet(Hush.Menu, true);
        tips.Quiet(Hush.Menu, false);
        tips.Point(50, 20);
        Pass(500);
        drop.Dropped();
        tips.After(drop);
        Pass(1499);
        var quiet = host.Shown.Count;
        Pass(1);
        Pass(500);
        drop.Dropped();
        tips.After(drop);
        tips.Left();
        tips.After(drop);
        tips.Point(50, 20);
        Pass(500);
        Update(tips, new ThermiConfig { Overlays = [new OverlayConfig { ClickThrough = true }] }, input);
        tips.Point(150, 20);
        Pass(600);

        var text = Text(input, 0);
        Assert.Equal(4, quiet);
        Assert.Equal([Text(input, 1), null, text, null, text, null, text, null], host.Shown.Select(tip => tip?.Text));
        Assert.False(tips.Speaks);
    }

    // A critical value's first glow lasts its while, at full strength in its middle, and comes no more for it; the
    // overlay's running, which OLED care moves it by, is the injected clock's.
    [Fact]
    public void AnOverlaysOwnClockGlowsOnceAndCountsItsRunning()
    {
        var clock = new OverlayClock(_time);
        OverlayItem[] critical = [new("CPU", "90 °C", Level.Critical, "100 °C") { Id = "cpu" }];

        Pass(60_000);
        var glow = clock.Glow(critical, animate: true);
        var glowing = clock.Glowing;
        Pass(300);
        var strength = glow?.Invoke("cpu");
        Pass(300);

        Assert.True(glowing);
        Assert.Equal(1, strength ?? 0, 3);
        Assert.Null(clock.Glow(critical, animate: true));
        Assert.False(clock.Glowing);
        Assert.Equal(TimeSpan.FromMilliseconds(60_600), clock.Running);
    }

    private static TrayInput Input(string recording) =>
        new(TestRecordings.Read(recording), ServiceState.Running, English, Texts.English, LightTaskbar: false, "");

    private static string Text(TrayInput input, int at)
    {
        var config = new ThermiConfig();
        return ValueTooltips.Text(config, 0, input, ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true).Tooltips[at]);
    }

    // The first overlay's items side by side, 100 px each.
    private static void Update(OverlayTips tips, ThermiConfig config, TrayInput input)
    {
        var scene = ThermiMark.Overlay(config, 0, input, windowOpenedOnce: true);
        tips.Update(config, 0, input, scene, [.. scene.Tooltips.Select((_, at) => new PaintedItem($"item{at}", new PillBox(at * 100, 0, 100, 40), default))]);
    }

    private void Pass(int milliseconds) => _time.Advance(TimeSpan.FromMilliseconds(milliseconds));

    // A desktop that draws each tooltip it is given, and runs what its timers post at once.
    private sealed class Tips(FakeTimeProvider time) : ITipsHost
    {
        public TimeProvider Time => time;

        public List<ShownTip?> Shown { get; } = [];

        public void Post(Action action) => action();

        public void Show(ShownTip? tip) => Shown.Add(tip);
    }
}
