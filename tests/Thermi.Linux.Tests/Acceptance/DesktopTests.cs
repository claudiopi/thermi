using System.Diagnostics;
using System.Text.Json.Nodes;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// What thermi does with the rest of the Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R18 and R20 to R22): its
/// settings window, its tray icons, its hotkeys and its notices.
/// </summary>
public sealed class DesktopTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    // A temperature past every line the recorded CPU reads, and one below it, so an edit changes the item's level.
    private const string Calm = """{"kind":"threshold","item":"cpu.temperature","warning":110,"critical":120}""";
    private const string Hot = """{"kind":"threshold","item":"cpu.temperature","warning":1,"critical":2}""";

    public enum Tray
    {
        Gnome,
        StatusNotifier,
    }

    public enum Hotkeys
    {
        Extension,
        Portal,
        X11,
        Held,
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task TheSettingsWindowWorksOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var session = desktop.Session;
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = Scene.OnWindow }] }, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        // Every way a person opens it, each with the window already open: one window still shows.
        var rows = Menus.OfOverlay(ThermiConfig.New, 0, new MenuState(false, false, ServiceState.Running, null, [], false));
        await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel, button: 3);
        await Accessible.ChooseAsync(session, rows.ToList().FindIndex(row => row.Entry == MenuEntry.Settings), cancel);
        await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel);
        await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel);
        await session.RunAsync(Built.Program("thermi"), ["settings"], cancel: cancel);
        var launcher = Path.Combine(Built.Root, "src", "Thermi.Linux", "Packaging", "io.github.claudiopi.thermi.desktop");
        await session.RunAsync("gio", ["launch", launcher], cancel: cancel);
        var window = Assert.Single(await Accessible.WindowsAsync(session, cancel), shown => shown.Program == "thermi");

        // A slider's change shows on the overlay within 300 ms, and undo takes it back.
        var shown = await desktop.ScreenshotAsync(cancel);
        var sliding = TimeProvider.System.GetTimestamp();
        await Accessible.SlideAsync(session, 0.25, cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var took = TimeProvider.System.GetElapsedTime(sliding);
        await desktop.Input.PressAsync("Control_L+z", cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var undone = await desktop.ScreenshotAsync(cancel);
        using var process = Process.GetProcessById(window.Process);
        await desktop.Input.PressAsync("Escape", cancel);
        await process.WaitForExitAsync(cancel);

        Assert.Equal(1, window.Windows);
        Assert.InRange(took.TotalMilliseconds, 0, 300);
        Assert.Equal(0, await Pictures.ChangedAsync(session, shown, undone, (0, 0, Monitor.Width, Monitor.Height), cancel));
        Assert.DoesNotContain(await Accessible.WindowsAsync(session, cancel), program => program.Process == window.Process);
    }

    [Theory]
    [InlineData(Tray.Gnome, Skip = "Waiting for its code")]
    [InlineData(Tray.StatusNotifier, Skip = "Waiting for its code")]
    public async Task TrayIconsShowOnEveryDesktop(Tray row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(row == Tray.Gnome ? DesktopKind.Gnome : DesktopKind.X11, [Monitor], cancel);
        var session = desktop.Session;
        var tray = row == Tray.StatusNotifier ? await TrayWatcher.ServeAsync(session, cancel) : null;
        var items = OverlayConfig.First.Items.Select((item, index) => index < 2 ? item with { Tray = true } : item).ToList();
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Items = items }] }, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        await session.RunAsync(Built.Program("thermi-cli"), ["edit", Calm], cancel: cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        var calm = await IconsAsync(desktop, tray, cancel);
        await session.RunAsync(Built.Program("thermi-cli"), ["edit", Hot], cancel: cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var hot = await IconsAsync(desktop, tray, cancel);
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        await desktop.ShowThermiAsync([], cancel);
        var back = await IconsAsync(desktop, tray, cancel);

        Assert.Equal(3, calm.Count);
        Assert.NotEqual(calm[1], hot[1]);
        Assert.Equal(hot, back);
        if (tray is not null)
        {
            var thermis = Menus.Thermis(ThermiConfig.New, new MenuState(false, false, ServiceState.Running, null, [], false));
            Assert.Equal(thermis.Count, await tray.MenuEntriesAsync(await tray.NextAsync(cancel), cancel));
        }
    }

    [Theory]
    [InlineData(Hotkeys.Extension, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.Portal, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.X11, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.Held, Skip = "Waiting for its code")]
    public async Task HotkeysWorkOnEveryDesktop(Hotkeys row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var kind = row switch
        {
            Hotkeys.Extension or Hotkeys.Held => DesktopKind.Gnome,
            Hotkeys.Portal => DesktopKind.KWin,
            Hotkeys.X11 => DesktopKind.X11,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(kind, [Monitor], cancel);
        var notices = await NoticeServer.ServeAsync(desktop.Session, cancel);
        // GNOME's own Ctrl+Alt+T opens a terminal, which the held row asks for.
        var hotkeys = new HotkeysConfig
        {
            Toggle = row == Hotkeys.Held ? "Ctrl+Alt+T" : "Ctrl+Alt+F9",
            NextStyle = "Ctrl+Alt+F10",
            ClickThrough = "Ctrl+Alt+F11",
            ResetMinMax = "Ctrl+Alt+F12",
        };
        await desktop.WriteSettingsAsync(
            ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = Scene.OnWindow }], Hotkeys = hotkeys }, cancel);
        await SensorFeed.ServeAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        if (row == Hotkeys.Portal)
        {
            // The portal asks the person once; its dialog's default button allows it.
            await desktop.Input.PressAsync("Return", cancel);
        }

        if (row == Hotkeys.Held)
        {
            Assert.Equal("thermi", (string?)(await notices.NextAsync(cancel))?["app"]);
            return;
        }

        var before = await Settings.ReadAsync(desktop.Session, cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F10", cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F11", cancel);
        var after = await Settings.ReadAsync(desktop.Session, cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F9", cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var hidden = await desktop.ScreenshotAsync(cancel);

        Assert.NotEqual(before.Overlays[0].Style, after.Overlays[0].Style);
        Assert.True(after.Overlays[0].ClickThrough);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, hidden, (Scene.OnWindow.X, Scene.OnWindow.Y, 200, 40), cancel));
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task NoticesAreTheDesktopsNotifications()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var session = desktop.Session;
        var notices = await NoticeServer.ServeAsync(session, cancel);
        var asked = await session.StartAsync("gdbus", ["monitor", "--session", "--dest", "org.gnome.Shell.Extensions"], cancel: cancel);
        await asked.StandardOutput.ReadLineAsync(cancel);
        var items = OverlayConfig.First.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Notify = true } : item).ToList();
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Items = items }] }, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        await session.RunAsync(Built.Program("thermi-cli"), ["edit", Hot], cancel: cancel);
        var started = TimeProvider.System.GetTimestamp();
        await desktop.ShowThermiAsync([], cancel);

        // The first start's, the extension's, and the critical item's once it stayed critical: one notification each.
        List<JsonNode> said = [];
        for (var notice = 0; notice < 3; notice++)
        {
            said.Add((await notices.NextAsync(cancel))!);
        }

        var critical = TimeProvider.System.GetElapsedTime(started);
        var extension = said.Single(notice => ((JsonArray)notice["actions"]!).Any(action => (string?)action == "install"));
        await notices.ClickAsync(extension, "install", cancel);
        string? install = null;
        while (install is null && await asked.StandardOutput.ReadLineAsync(cancel) is { } line)
        {
            install = line.Contains("InstallRemoteExtension", StringComparison.Ordinal) ? line : null;
        }

        Assert.All(said, notice => Assert.Equal("thermi", (string?)notice["app"]));
        Assert.Equal(3, said.Select(notice => (string?)notice["summary"]).Distinct(StringComparer.Ordinal).Count());
        Assert.True(critical >= NotifyMe.After, $"The critical notice came {critical} after the start.");
        Assert.NotNull(install);
    }

    // What each tray icon shows: on GNOME's top bar, its right part's pixels; in a tray, each icon's id and picture.
    private static async Task<List<string>> IconsAsync(TestDesktop desktop, TrayWatcher? tray, CancellationToken cancel)
    {
        if (tray is null)
        {
            var screen = await desktop.ScreenshotAsync(cancel);
            var crop = Path.Combine(desktop.Session.Home, $"bar-{Guid.NewGuid():N}.txt");
            await desktop.Session.RunAsync("convert", [screen, "-crop", $"400x40+{Monitor.Width - 400}+0", $"txt:{crop}"], cancel: cancel);
            return [.. (await File.ReadAllTextAsync(crop, cancel)).Split('\n').Chunk(400 * 40 / 3).Select(part => string.Join('\n', part))];
        }

        List<string> icons = [];
        for (var icon = 0; icon < 3; icon++)
        {
            var item = await tray.NextAsync(cancel);
            icons.Add(await tray.PropertyAsync(item, "Id", cancel) + await tray.PropertyAsync(item, "IconPixmap", cancel));
        }

        return icons;
    }
}
