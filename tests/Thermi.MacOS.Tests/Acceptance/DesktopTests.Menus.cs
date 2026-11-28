using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.MacOS.Tests.Desktop;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>thermi's items in the menu bar, its hotkeys and its notices on the Mac.</summary>
public sealed partial class DesktopTests
{
    // A temperature past every line the recorded CPU reads, and one below it, so an edit changes the item's level.
    private const string Calm = """{"kind":"threshold","item":"cpu.temperature","warning":110,"critical":120}""";
    private const string Hot = """{"kind":"threshold","item":"cpu.temperature","warning":1,"critical":2}""";

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task MenuBarItemsShowOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var items = OverlayConfig.First.Items.Select((item, index) => index < 2 ? item with { Tray = true } : item).ToList();
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Items = items }] }, cancel);
        await mac.Session.RunAsync("thermi-cli", ["edit", Calm], cancel: cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);

        // thermi's icon and one item for each value, each its own window at the menu bar's level, in its colors.
        var calm = await MenuBarAsync(mac, thermi.Id, cancel);
        await mac.Session.RunAsync("thermi-cli", ["edit", Hot], cancel: cancel);
        await TestMac.PaintedAsync(thermi, cancel);
        var hot = await MenuBarAsync(mac, thermi.Id, cancel);
        await MenuReader.OpenStatusItemAsync(mac.Session, thermi.Id, cancel);
        var menu = await MenuReader.EntriesAsync(mac.Session, thermi.Id, cancel);
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        thermi = await mac.ShowThermiAsync([], cancel);
        await TestMac.PaintedAsync(thermi, cancel);
        var back = await MenuBarAsync(mac, thermi.Id, cancel);

        Assert.Equal(3, calm.Count);
        Assert.NotEqual(0, calm[1].Differing(hot[1]));
        Assert.Equal(hot.Count, back.Count);
        Assert.All(hot.Zip(back), pair => Assert.Equal(0, pair.First.Differing(pair.Second)));
        Assert.Equal(Menus.Thermis(ThermiConfig.New, new MenuState(false, false, ServiceState.Running, null, [], false)).Count, menu.Count);
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task HotkeysWorkOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(null, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0].Visible;
        var area = (main.X + 200, main.Y + 200, 200, 40);
        // Another program holds Ctrl+Alt+F11 before thermi starts: thermi refuses it, as Windows does a combination another
        // program holds.
        await Input.HoldHotkeyAsync(mac.Session, Input.F11, Input.CarbonControlOption, cancel);
        var hotkeys = new HotkeysConfig { Toggle = "Ctrl+Alt+F9", NextStyle = "Ctrl+Alt+F10", ClickThrough = "Ctrl+Alt+F11", ResetMinMax = "Ctrl+Alt+F12" };
        // The CPU's temperature shows its highest, which only Reset min/max takes back to now.
        var items = OverlayConfig.First.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Stat = ItemStat.Max } : item).ToList();
        var overlay = OverlayConfig.First with { Position = new(main.X + 200, main.Y + 200), Items = items };
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay], Hotkeys = hotkeys }, cancel);
        var feed = await SensorFeed.ServeAsync(mac.Session, SensorFeed.MacMiniLoaded, cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);
        var refused = await Traced.FirstAsync(thermi.Error, Traced.HotkeyRefused, cancel);

        var before = await mac.ReadSettingsAsync(cancel);
        var styled = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await Input.KeyAsync(mac.Session, Input.F10, cancel, Input.ControlOption);
        await styled;
        var after = await mac.ReadSettingsAsync(cancel);
        var reset = await ResetsAsync(mac, thermi, feed, area, cancel);
        var shown = await Screen.CaptureAsync(mac.Session, area, cancel);
        var hidden = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
        await Input.KeyAsync(mac.Session, Input.F9, cancel, Input.ControlOption);
        await hidden;
        var gone = await Screen.CaptureAsync(mac.Session, area, cancel);

        Assert.Equal("clickThrough", refused.About);
        Assert.NotEqual(before.Overlays[0].Style, after.Overlays[0].Style);
        Assert.NotEqual(0, reset);
        Assert.NotEqual(0, gone.Differing(shown));
    }

    // The Mac cools, from the loaded recording to the one at rest: the highest stays as it was, until Reset min/max's hotkey
    // takes it to now. How much of the overlay the reset changed, two of thermi's seconds after the press.
    private static async Task<int> ResetsAsync(
        TestMac mac, SessionProcess thermi, SessionProcess feed, (int X, int Y, int Width, int Height) area, CancellationToken cancel)
    {
        feed.Kill();
        await feed.WaitForExitAsync(cancel);
        var cooled = Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: 2);
        await SensorFeed.ServeAsync(mac.Session, SensorFeed.MacMini, cancel);
        await cooled;
        var highest = await Screen.CaptureAsync(mac.Session, area, cancel);
        var reset = Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: 2);
        await Input.KeyAsync(mac.Session, Input.F12, cancel, Input.ControlOption);
        await reset;
        return (await Screen.CaptureAsync(mac.Session, area, cancel)).Differing(highest);
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task NoticesAreTheMacsNotifications()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var items = OverlayConfig.First.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Notify = true } : item).ToList();
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Items = items }] }, cancel);
        await mac.Session.RunAsync("thermi-cli", ["edit", Hot], cancel: cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);

        // The first start's notice at once, and the critical item's once it stayed critical for NotifyMe's while, by thermi's
        // own seconds, and not before: each a notification from thermi.
        var early = await NoticesAfterAsync(mac, thermi, (int)NotifyMe.After.TotalSeconds - 2, cancel);
        var shown = await NoticesAfterAsync(mac, thermi, 4, cancel);
        var window = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        var (_, title, buttons) = shown.First(notice => notice.Buttons.Length > 0);
        await NoticeReader.ClickAsync(mac.Session, title, buttons[0], cancel);

        Assert.Single(early.Select(notice => notice.Title).Distinct(StringComparer.Ordinal));
        Assert.Equal(2, shown.Select(notice => notice.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.NotNull(await window);
    }

    // thermi's notifications on screen once it painted as many more seconds.
    private static async Task<List<(string App, string Title, string[] Buttons)>> NoticesAfterAsync(
        TestMac mac, SessionProcess thermi, int seconds, CancellationToken cancel)
    {
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: seconds);
        return [.. (await NoticeReader.ShownAsync(mac.Session, cancel)).Where(notice => notice.App == "thermi")];
    }

    // Each of thermi's windows at the menu bar's level, left to right, as the screen shows it.
    private static async Task<List<Picture>> MenuBarAsync(TestMac mac, int pid, CancellationToken cancel)
    {
        var items = (await Screen.WindowsAsync(mac.Session, pid, cancel))
            .Where(window => window.Level == Screen.MenuBarLevel).OrderBy(window => window.Bounds.X);
        List<Picture> shown = [];
        foreach (var (_, bounds) in items)
        {
            shown.Add(await Screen.CaptureAsync(mac.Session, bounds, cancel));
        }

        return shown;
    }
}
