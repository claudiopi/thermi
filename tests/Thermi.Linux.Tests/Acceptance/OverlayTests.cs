using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// The overlay on each Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R8 to R13, R17 and R24): above every window
/// and panel as <c>thermi preview</c> paints it, clicks through where it draws nothing, its menu, and its unit.
/// </summary>
public sealed class OverlayTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    public enum GnomeCase
    {
        WaylandAt150,
        X11,
        FullScreen,
        SensorsStopped,
    }

    public enum LayerCase
    {
        KWin,
        SwayAt150,
        FullScreen,
        Neither,
    }

    [Theory]
    [InlineData(GnomeCase.WaylandAt150, Skip = "Waiting for its code")]
    [InlineData(GnomeCase.X11, Skip = "Waiting for its code")]
    [InlineData(GnomeCase.FullScreen, Skip = "Waiting for its code")]
    [InlineData(GnomeCase.SensorsStopped, Skip = "Waiting for its code")]
    public async Task TheOverlayShowsAboveEveryWindowOnGnome(GnomeCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var scale = row == GnomeCase.WaylandAt150 ? 1.5 : 1;
        await using var desktop = await TestDesktop.StartAsync(
            row == GnomeCase.X11 ? DesktopKind.GnomeX11 : DesktopKind.Gnome, [Monitor with { Scale = scale }], cancel);

        var scene = await Scene.ShowAsync(desktop, scale, row == GnomeCase.FullScreen, row == GnomeCase.SensorsStopped, cancel);

        if (row == GnomeCase.FullScreen)
        {
            Assert.Equal(0, await scene.ChangedAsync(cancel));
            return;
        }

        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        Assert.Equal("click 1", await scene.ClickThroughAsync(cancel));
    }

    [Theory]
    [InlineData(LayerCase.KWin, Skip = "Waiting for its code")]
    [InlineData(LayerCase.SwayAt150, Skip = "Waiting for its code")]
    [InlineData(LayerCase.FullScreen, Skip = "Waiting for its code")]
    [InlineData(LayerCase.Neither, Skip = "Waiting for its code")]
    public async Task TheOverlayShowsAboveEveryWindowOnLayerShell(LayerCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var scale = row == LayerCase.SwayAt150 ? 1.5 : 1;
        var kind = row switch
        {
            LayerCase.SwayAt150 => DesktopKind.Sway,
            LayerCase.Neither => DesktopKind.BareWayland,
            LayerCase.KWin or LayerCase.FullScreen => DesktopKind.KWin,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(kind, [Monitor with { Scale = scale }], cancel);
        if (row == LayerCase.Neither)
        {
            await NeitherSurfaceShowsAsync(desktop, cancel);
            return;
        }

        var scene = await Scene.ShowAsync(desktop, scale, fullScreen: false, stopped: false, cancel);
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        Assert.Equal("click 1", await scene.ClickThroughAsync(cancel));
        if (row == LayerCase.FullScreen)
        {
            // A window that goes full screen in front of the overlay covers it, as every window of a layer below the top one.
            await desktop.OpenWindowAsync(fullScreen: true, cancel);
            var covered = scene with { Screen = await desktop.ScreenshotAsync(cancel) };
            Assert.NotEqual(0, await covered.DifferingAtAsync(Scene.OnWindow, cancel));
        }
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task TheOverlayShowsAboveEveryWindowOnX11()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [Monitor], cancel);

        var scene = await Scene.ShowAsync(desktop, 1, fullScreen: false, stopped: false, cancel, clickThrough: true);

        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        // Off the taskbar and the window switcher: every window of thermi's says so to the window manager.
        var windows = await desktop.Session.RunAsync("xdotool", ["search", "--class", "thermi"], cancel: cancel);
        var ids = windows.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            var state = await desktop.Session.RunAsync("xprop", ["-id", id, "_NET_WM_STATE"], cancel: cancel);
            Assert.Contains("_NET_WM_STATE_SKIP_TASKBAR", state.Out, StringComparison.Ordinal);
            Assert.Contains("_NET_WM_STATE_SKIP_PAGER", state.Out, StringComparison.Ordinal);
        }

        // Click-through, a click where it draws passes too: the middle of the overlay on the window.
        var (width, height) = await Pictures.SizeAsync(desktop.Session, scene.Picture, cancel);
        await desktop.Input.ClickAsync(Scene.OnWindow.X + (width / 2), Scene.OnWindow.Y + (height / 2), cancel);
        Assert.Equal("click 1", await scene.Window.NextAsync(cancel));
        await desktop.OpenWindowAsync(fullScreen: true, cancel);
        var covered = scene with { Screen = await desktop.ScreenshotAsync(cancel) };
        Assert.NotEqual(0, await covered.DifferingAtAsync(Scene.OnPanel, cancel));
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task AnExtensionOfAnotherVersionShowsNothing()
    {
        var cancel = TestContext.Current.CancellationToken;
        var other = Directory.CreateTempSubdirectory("thermi-extension-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(TestShell.Thermi))
            {
                File.Copy(file, Path.Combine(other, Path.GetFileName(file)));
            }

            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(other, "metadata.json"), cancel))!;
            metadata["thermi-protocol"] = (int)metadata["thermi-protocol"]! - 1;
            await File.WriteAllTextAsync(Path.Combine(other, "metadata.json"), metadata.ToJsonString(), cancel);

            await AnotherVersionShowsNothingAsync(other, cancel);
            await NoneIsInstalledFromTheStoreAsync(cancel);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, Skip = "Waiting for its code")]
    [InlineData(false, Skip = "Waiting for its code")]
    public async Task TheOverlaysMenuIsTheShellsOwn(bool gnome)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(gnome ? DesktopKind.Gnome : DesktopKind.KWin, [Monitor], cancel);
        using var units = await UserUnits.StartAsync(desktop.Session, cancel);
        var config = ThermiConfig.New with
        {
            Overlays = [OverlayConfig.First with { Position = Scene.OnPanel }],
            Hotkeys = new HotkeysConfig { Toggle = "Ctrl+Alt+T" },
        };
        await desktop.WriteSettingsAsync(config, cancel);
        await SensorFeed.ServeAsync(desktop.Session, SensorFeed.Desktop, cancel);
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await units.PaintedAsync(cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var rows = Menus.OfOverlay(config, 0, new MenuState(false, false, ServiceState.Running, null, [], false));

        Assert.Equal((gnome ? "gnome-shell" : "thermi", rows.Count), await OpenMenuAsync(desktop, cancel));
        await ChooseAsync(desktop, rows, MenuEntry.HideOverlays, cancel);
        await units.PaintedAsync(cancel);
        var hidden = await desktop.ScreenshotAsync(cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+t", cancel);
        await units.PaintedAsync(cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        await ChooseAsync(desktop, rows, MenuEntry.TaskManager, cancel);
        var monitor = await desktop.Session.RunAsync(
            "gdbus", ["wait", "--session", gnome ? "org.gnome.SystemMonitor" : "org.kde.plasma-systemmonitor"], cancel: cancel);
        var stopped = await units.WatchStopAsync(cancel);
        await ChooseAsync(desktop, rows, MenuEntry.Exit, cancel);
        await stopped();
        // Not "auto-restart": a clean exit schedules no start again.
        var exited = await units.SystemctlAsync(["show", "-p", "SubState", "--value", "thermi.service"], cancel);

        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, before, hidden, (Scene.OnPanel.X, 0, 400, 100), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, shown, (0, 0, Monitor.Width, Monitor.Height), cancel));
        Assert.Equal(0, monitor.Exit);
        Assert.Equal("dead", exited);

        // Killed, the unit starts it again, and one overlay runs.
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await units.PaintedAsync(cancel);
        await units.SystemctlAsync(["kill", "--signal=KILL", "thermi.service"], cancel);
        await units.PaintedAsync(cancel);
        Assert.Single(await units.ProcessesAsync(cancel));
    }

    // Weston offers neither the extension nor layer surfaces: thermi shows nothing, says why in one notice, and not again
    // the next time it starts.
    private static async Task NeitherSurfaceShowsAsync(TestDesktop desktop, CancellationToken cancel)
    {
        var notices = await NoticeServer.ServeAsync(desktop.Session, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        for (var start = 0; start < 2; start++)
        {
            var thermi = await desktop.StartThermiAsync([], cancel);
            await thermi.WaitForExitAsync(cancel);
            Assert.NotEqual(0, thermi.ExitCode);
        }

        var said = await notices.NextAsync(cancel);
        Assert.NotNull(said);
        Assert.Equal("thermi", (string?)said["app"]);
        var status = await desktop.Session.RunAsync(Built.Program("thermi-cli"), ["status", "--schema", "1"], cancel: cancel);
        Assert.Contains("\"overlay-cannot-show\"", status.Out, StringComparison.Ordinal);
    }

    private static async Task AnotherVersionShowsNothingAsync(string other, CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], other, cancel);
        var notices = await NoticeServer.ServeAsync(desktop.Session, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        await desktop.StartThermiAsync([], cancel);

        var notice = await notices.NextAsync(cancel);
        var after = await desktop.ScreenshotAsync(cancel);
        Assert.Equal("thermi", (string?)notice?["app"]);
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, after, (0, 40, Monitor.Width, Monitor.Height - 40), cancel));
    }

    // With no extension, thermi asks the Shell to install its own from extensions.gnome.org, which GNOME confirms itself.
    private static async Task NoneIsInstalledFromTheStoreAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var asked = await desktop.Session.StartAsync("gdbus", ["monitor", "--session", "--dest", "org.gnome.Shell.Extensions"], cancel: cancel);
        await asked.StandardOutput.ReadLineAsync(cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        await desktop.StartThermiAsync([], cancel);

        var uuid = (string)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(TestShell.Thermi, "metadata.json"), cancel))!["uuid"]!;
        while (await asked.StandardOutput.ReadLineAsync(cancel) is { } line)
        {
            if (line.Contains("InstallRemoteExtension", StringComparison.Ordinal))
            {
                Assert.Contains($"'{uuid}'", line, StringComparison.Ordinal);
                return;
            }
        }

        Assert.Fail("thermi never asked the Shell to install its extension.");
    }

    // A right-click on the first overlay's first pill, which sits at its left, where every overlay starts.
    private static async Task<(string Program, int Entries)> OpenMenuAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.Input.ClickAsync(Scene.OnPanel.X + 8, Scene.OnPanel.Y + 8, cancel, button: 3);
        return Assert.Single(await Accessible.OpenMenusAsync(desktop.Session, cancel));
    }

    private static async Task ChooseAsync(TestDesktop desktop, IReadOnlyList<MenuRow> rows, MenuEntry entry, CancellationToken cancel)
    {
        await OpenMenuAsync(desktop, cancel);
        await Accessible.ChooseAsync(desktop.Session, rows.ToList().FindIndex(row => row.Entry == entry), cancel);
    }
}
