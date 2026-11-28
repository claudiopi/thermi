using System.Diagnostics;
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
        var other = (main.X + 400, main.Y + 100, 600, 400);

        // Every way a person opens it, first with it closed and then behind another app's window: one window, in front and
        // focused, which once closed leaves no process.
        foreach (var way in WaysToOpen(mac, thermi, pill, cancel))
        {
            await OpensInFrontAsync(mac, thermi, other, way, cancel);
        }

        // With the window open, an edit reaches the overlay and its file as on Windows.
        var shown = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await mac.Session.RunAsync(Built.Thermi, ["settings"], cancel: cancel);
        await shown;
        var window = await WindowAsync(mac, thermi, cancel);
        var before = (await mac.ReadSettingsAsync(cancel)).Overlays[0].Size;
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await Input.CommandWheelAsync(mac.Session, pill.X, pill.Y, 1, cancel);
        await applied;
        await ClosedAsync(mac, window, cancel);

        Assert.NotEqual(before, (await mac.ReadSettingsAsync(cancel)).Overlays[0].Size);
        Assert.Equal(new[] { thermi.Id }, await ThermisAsync(mac, cancel));
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

        // The service stopped, as the person may stop it, in both pictures: the window offers to start it again, which needs
        // no prompt, and the row's look alone tells them apart.
        await Launchd.StopServiceAsync(mac.Session, cancel);
        await AppearanceAsync(mac, Look.Light, cancel);
        var (plain, _) = await OpenWindowAsync(mac, cancel);
        await AppearanceAsync(mac, row, cancel);
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

    // The way opens the window in front of another app's, with the focus; again, with the window behind another app's, it
    // brings the same one to the front; and closed, its process ends.
    private static async Task OpensInFrontAsync(
        TestMac mac, SessionProcess thermi, (int X, int Y, int Width, int Height) other, Func<Task> way, CancellationToken cancel)
    {
        var opened = new List<int>();
        for (var time = 0; time < 2; time++)
        {
            var shown = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
            var front = await Screen.ShowWindowAsync(mac.Session, other, fullScreen: false, cancel);
            await way();
            await shown;
            opened.Add(await WindowAsync(mac, thermi, cancel));
            Assert.Equal(opened[^1], await Screen.FrontAsync(mac.Session, cancel));
            front.Kill();
        }

        Assert.Equal(opened[0], opened[1]);
        await ClosedAsync(mac, opened[0], cancel);
    }

    // The window's process, the one thermi's of the session that is not the overlay's: there is one window, or none.
    private static async Task<int> WindowAsync(TestMac mac, SessionProcess thermi, CancellationToken cancel) =>
        Assert.Single((await ThermisAsync(mac, cancel)).Where(pid => pid != thermi.Id));

    // Esc closes the window in front, whose process ends.
    private static async Task ClosedAsync(TestMac mac, int window, CancellationToken cancel)
    {
        using var process = Process.GetProcessById(window);
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        await process.WaitForExitAsync(cancel).WaitAsync(Bound.End, TimeProvider.System, cancel);
    }

    // thermi's processes in the session, by their pids: the overlay's, and the window's while it is open.
    private static async Task<int[]> ThermisAsync(TestMac mac, CancellationToken cancel)
    {
        var found = await mac.Session.RunAsync("pgrep", ["-f", Built.Thermi], cancel: cancel);
        return [.. found.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(pid => int.Parse(pid, CultureInfo.InvariantCulture)).Order()];
    }

    // The Mac's appearance as the row sets it in System Settings: Dark or Light, the purple accent, Increase contrast.
    private static async Task AppearanceAsync(TestMac mac, Look row, CancellationToken cancel)
    {
        const string Global = "NSGlobalDomain";
        const string Access = "com.apple.universalaccess";
        (string Verb, string Domain, string[] Setting)[] settings = row switch
        {
            Look.Dark or Look.ThemeChosen => [("write", Global, ["AppleInterfaceStyle", "Dark"])],
            Look.OtherAccent => [("delete", Global, ["AppleInterfaceStyle"]), ("write", Global, ["AppleAccentColor", "-int", "5"])],
            Look.IncreaseContrast => [("delete", Global, ["AppleInterfaceStyle"]), ("write", Access, ["increaseContrast", "-bool", "true"])],
            Look.Light =>
            [
                ("delete", Global, ["AppleInterfaceStyle"]), ("delete", Global, ["AppleAccentColor"]),
                ("write", Access, ["increaseContrast", "-bool", "false"]),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown look."),
        };
        foreach (var (verb, domain, setting) in settings)
        {
            await mac.PreferAsync(verb, domain, setting, cancel);
        }

        // Apps read the appearance when the Mac says it changed, as System Settings does.
        await mac.Session.RunAsync("notifyutil", ["-p", "AppleInterfaceThemeChangedNotification"], cancel: cancel);
    }
}
