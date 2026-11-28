using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.MacOS.Tests.Desktop;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// What thermi does with the rest of the Mac (<c>docs/specs/008-macos/spec.md</c>, R17 to R21): its settings window and
/// how it follows the Mac's look, its items in the menu bar, its hotkeys and its notices.
/// </summary>
public sealed partial class DesktopTests
{
    public enum Look
    {
        Light,
        Dark,
        OtherAccent,
        IncreaseContrast,
        ThemeChosen,
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheSettingsWindowWorksOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0].Visible;
        var pill = (X: main.X + 208, Y: main.Y + 208);
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = new(main.X + 200, main.Y + 200) }] }, cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);
        var other = await Screen.ShowWindowAsync(mac.Session, (main.X + 400, main.Y + 100, 600, 400), fullScreen: false, cancel);

        // Every way a person opens it, first with it closed and then behind another window: it comes to the front, focused.
        foreach (var way in WaysToOpen(mac, thermi, pill, cancel))
        {
            await OpensInFrontAsync(mac, thermi, other, way, cancel);
        }

        // With the window open, an edit reaches the overlay and its file as on Windows; closed, no process of it is left.
        var before = (await mac.ReadSettingsAsync(cancel)).Overlays[0].Size;
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await Input.CommandWheelAsync(mac.Session, pill.X, pill.Y, 1, cancel);
        await applied;
        var processes = await ThermisAsync(mac, cancel);
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        await TestMac.PaintedAsync(thermi, cancel);

        Assert.NotEqual(before, (await mac.ReadSettingsAsync(cancel)).Overlays[0].Size);
        Assert.Equal(new[] { thermi.Id }, await ThermisAsync(mac, cancel));
        Assert.Contains(thermi.Id, processes);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Look.Light, Skip = "Waiting for its code")]
    [InlineData(Look.Dark, Skip = "Waiting for its code")]
    [InlineData(Look.OtherAccent, Skip = "Waiting for its code")]
    [InlineData(Look.IncreaseContrast, Skip = "Waiting for its code")]
    [InlineData(Look.ThemeChosen, Skip = "Waiting for its code")]
    public async Task TheWindowFollowsTheMac(Look row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(null, cancel);
        await Launchd.LoadServiceAsync(mac.Session, SensorFeed.MacMini, cancel);
        var theme = row == Look.ThemeChosen ? WindowTheme.Light : WindowTheme.System;
        await mac.WriteSettingsAsync(ThermiConfig.New with { WindowTheme = theme }, cancel);
        await AppearanceAsync(mac.Session, Look.Light, cancel);
        var (plain, _) = await OpenWindowAsync(mac, cancel);

        // The service stopped, as the person may stop it: the window offers to start it again, which needs no prompt.
        var (_, user) = await mac.Session.DomainsAsync(cancel);
        await mac.Session.RunAsync("launchctl", ["kill", "SIGTERM", $"{user}/{Launchd.ServiceLabel}"], cancel: cancel);
        await AppearanceAsync(mac.Session, row, cancel);
        var (page, window) = await OpenWindowAsync(mac, cancel);
        await WindowPage.PressAsync(mac.Session, window, Texts.English.Of(WindowText.StartIt), cancel);
        var status = JsonNode.Parse((await mac.Session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;

        // Dark for the Mac's dark, unless Settings chose a theme; the accent and the contrast change what the page draws.
        Assert.Equal(row == Look.Dark, page.Brightness() < 128);
        Assert.Equal(row is Look.OtherAccent or Look.IncreaseContrast or Look.Dark, page.Differing(plain) > 0);
        Assert.Equal("running", (string?)status["service"]);
    }

    // The overlay's menu, thermi's in the menu bar, a double-click on the overlay, thermi settings, and opening the app.
    private static Func<Task>[] WaysToOpen(TestMac mac, SessionProcess thermi, (int X, int Y) pill, CancellationToken cancel)
    {
        var rows = Menus.OfOverlay(ThermiConfig.New, 0, new MenuState(false, false, ServiceState.Running, null, [], false)).ToList();
        var thermis = Menus.Thermis(ThermiConfig.New, new MenuState(false, false, ServiceState.Running, null, [], false)).ToList();
        return
        [
            async () =>
            {
                await Input.MenuClickAsync(mac.Session, pill.X, pill.Y, control: false, cancel);
                await MenuReader.ChooseAsync(mac.Session, thermi.Id, rows.FindIndex(row => row.Entry == MenuEntry.Settings), cancel);
            },
            async () =>
            {
                await MenuReader.OpenStatusItemAsync(mac.Session, thermi.Id, cancel);
                await MenuReader.ChooseAsync(mac.Session, thermi.Id, thermis.FindIndex(row => row.Entry == MenuEntry.Settings), cancel);
            },
            () => Input.ClickAsync(mac.Session, pill.X, pill.Y, cancel, times: 2),
            () => mac.Session.RunAsync(Built.Thermi, ["settings"], cancel: cancel),
            () => mac.Session.RunAsync("open", [Built.App], cancel: cancel),
        ];
    }

    // The window, opened by thermi settings as a person's shell does, and what it shows once shown; one open before ends.
    private static async Task<(Picture Page, int Pid)> OpenWindowAsync(TestMac mac, CancellationToken cancel)
    {
        await mac.Session.RunAsync("pkill", ["-f", $"{Built.Thermi} settings"], cancel: cancel);
        var window = mac.Session.Start(Built.Thermi, ["settings", "--trace"]);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        var (_, bounds) = (await Screen.WindowsAsync(mac.Session, window.Id, cancel))[0];
        return (await Screen.CaptureAsync(mac.Session, bounds, cancel), window.Id);
    }

    // The way opens the window, which comes in front of the other app's and has the focus; a second way shows the same one.
    private static async Task OpensInFrontAsync(TestMac mac, SessionProcess thermi, SessionProcess other, Func<Task> way, CancellationToken cancel)
    {
        foreach (var behind in new[] { false, true })
        {
            var shown = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
            if (behind)
            {
                await mac.Session.RunAsync("open", ["-a", "Terminal"], cancel: cancel);
            }

            await way();
            await shown;
            var front = await Screen.FrontAsync(mac.Session, cancel);
            Assert.Contains(front, await ThermisAsync(mac, cancel));
            Assert.NotEqual(other.Id, front);
        }
    }

    // thermi's processes in the session, by their pids: the overlay's, and the window's while it is open.
    private static async Task<int[]> ThermisAsync(TestMac mac, CancellationToken cancel)
    {
        var found = await mac.Session.RunAsync("pgrep", ["-f", Built.Thermi], cancel: cancel);
        return [.. found.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(pid => int.Parse(pid, CultureInfo.InvariantCulture)).Order()];
    }

    // The Mac's appearance as the row sets it in System Settings: Dark or Light, the purple accent, Increase contrast.
    private static async Task AppearanceAsync(OwnSession session, Look row, CancellationToken cancel)
    {
        string[][] settings = row switch
        {
            Look.Dark or Look.ThemeChosen => [["write", "-g", "AppleInterfaceStyle", "Dark"]],
            Look.OtherAccent => [["delete", "-g", "AppleInterfaceStyle"], ["write", "-g", "AppleAccentColor", "-int", "5"]],
            Look.IncreaseContrast => [["delete", "-g", "AppleInterfaceStyle"], ["write", "com.apple.universalaccess", "increaseContrast", "-bool", "true"]],
            Look.Light =>
            [
                ["delete", "-g", "AppleInterfaceStyle"], ["delete", "-g", "AppleAccentColor"],
                ["write", "com.apple.universalaccess", "increaseContrast", "-bool", "false"],
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown look."),
        };
        foreach (var setting in settings)
        {
            await session.RunAsync("defaults", setting, cancel: cancel);
        }

        // Apps read the appearance when the Mac says it changed, as System Settings does.
        await session.RunAsync("notifyutil", ["-p", "AppleInterfaceThemeChangedNotification"], cancel: cancel);
    }
}
