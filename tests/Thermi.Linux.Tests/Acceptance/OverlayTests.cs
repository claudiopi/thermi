using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// The overlay on each Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R8 to R13, R17 and R24): above every window
/// and panel as <c>thermi preview</c> paints it, clicks through where it draws nothing, its menu, and its unit.
/// </summary>
public sealed class OverlayTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    private static readonly (int X, int Y, int Width, int Height) UnderTheBar = (Scene.OnWindow.X, Scene.OnWindow.Y, 400, 200);

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
    [InlineData(GnomeCase.WaylandAt150)]
    [InlineData(GnomeCase.X11)]
    [InlineData(GnomeCase.FullScreen)]
    [InlineData(GnomeCase.SensorsStopped)]
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
    [InlineData(LayerCase.KWin)]
    [InlineData(LayerCase.SwayAt150)]
    [InlineData(LayerCase.FullScreen)]
    [InlineData(LayerCase.Neither)]
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
            Assert.Equal(0, await CoveredAsync(scene, Scene.OnWindow, cancel));
        }
    }

    [Fact]
    public async Task TheOverlayShowsAboveEveryWindowOnX11()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [Monitor], cancel);

        var scene = await Scene.ShowAsync(desktop, 1, fullScreen: false, stopped: false, cancel, clickThrough: true);

        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        // Off the taskbar and the window switcher: every window of thermi's process says so to the window manager.
        var windows = await desktop.Session.RunAsync("xdotool", ["search", "--onlyvisible", "--pid", $"{scene.Thermi.Id}"], cancel: cancel);
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
        Assert.Equal("click 1", await scene.ClickAsync(Scene.OnWindow.X + (width / 2), Scene.OnWindow.Y + (height / 2), cancel));
        await desktop.OpenWindowAsync(fullScreen: true, cancel);
        Assert.Equal(0, await CoveredAsync(scene, Scene.OnPanel, cancel));
    }

    // A full-screen window in front covers the overlay: where it was, the screen shows what it shows once thermi is gone.
    private static async Task<long> CoveredAsync(Scene scene, Position at, CancellationToken cancel)
    {
        var covered = await scene.Desktop.ScreenshotAsync(cancel);
        scene.Thermi.Kill();
        await scene.Thermi.WaitForExitAsync(cancel);
        var alone = await scene.Desktop.ScreenshotAsync(cancel);
        var (width, height) = await Pictures.SizeAsync(scene.Desktop.Session, scene.Picture, cancel);
        return await Pictures.ChangedAsync(scene.Desktop.Session, covered, alone, (at.X, at.Y, width, height), cancel);
    }

    [Fact]
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
            // Another protocol's signals may have other shapes: this one sends Ready and Pointer with nothing in them once
            // thermi takes its name.
            var code = await File.ReadAllTextAsync(Path.Combine(other, "extension.js"), cancel);
            const string Appeared = "this._owner = owner;";
            Assert.Contains(Appeared, code, StringComparison.Ordinal);
            var sends = "['Ready', 'Pointer'].forEach(signal => this._emit(signal, '()', []));";
            code = code.Replace(Appeared, $"{Appeared} {sends}", StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(other, "extension.js"), code, cancel);

            await AnotherVersionShowsNothingAsync(other, cancel);
            await NoneIsInstalledFromTheStoreAsync(cancel);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false, Skip = "Waiting for its code")]
    public async Task TheOverlaysMenuIsTheShellsOwn(bool gnome)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(gnome ? DesktopKind.Gnome : DesktopKind.KWin, [Monitor], cancel);
        var units = await UserUnits.StartAsync(desktop.Session, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        var config = ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] };
        await desktop.WriteSettingsAsync(config, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var started = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        var running = await units.StartWaitingAsync(cancel);
        if (await Task.WhenAny(started, running.WaitForExitAsync(cancel)) != started)
        {
            Assert.Fail($"thermi's unit ended before it showed: {await running.Error.ToEndAsync(cancel)}"
                + await units.SystemctlAsync(["status", "--no-pager", "thermi.service"], cancel));
        }
        var shownFirst = await desktop.ScreenshotAsync(cancel);
        var pill = config.Overlays[0].Items[0].Id;
        var rows = Menus.OfOverlay(config, 0, new MenuState(false, false, ServiceState.Running, null, [], false) { CanCopy = false }, pill);

        Assert.Equal((gnome ? "gnome-shell" : "thermi", rows.Count), await OpenMenuAsync(desktop, cancel));
        // Hidden from the menu, the overlays are taken back with the button of the notice that says so.
        var hiding = Traced.NextAsync(units.Trace, Traced.Hidden, cancel);
        var told = notices.NextAsync("show-it", cancel);
        // From the menu open already: a second right-click while it is open is a click outside it, which closes it.
        await Accessible.ChooseAsync(desktop, IndexOf(rows, MenuEntry.HideOverlays), cancel);
        await hiding;
        var hidden = await desktop.ScreenshotAsync(cancel);
        var showing = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        await notices.ClickAsync(await told, "show-it", cancel);
        await showing;
        var shown = await desktop.ScreenshotAsync(cancel);
        var monitorName = gnome ? "org.gnome.SystemMonitor" : "org.kde.plasma-systemmonitor";
        await units.AddProgramAsync(gnome ? "gnome-system-monitor" : "plasma-systemmonitor", monitorName, cancel);
        await ChooseAsync(desktop, rows, MenuEntry.TaskManager, cancel);
        var monitor = await desktop.Session.RunAsync("gdbus", ["wait", "--session", monitorName], cancel: cancel);
        await ChooseAsync(desktop, rows, MenuEntry.Exit, cancel);
        // systemctl --wait returns once the unit went inactive; not "auto-restart": a clean exit starts nothing again.
        await running.WaitForExitAsync(cancel);
        var exited = await units.SystemctlAsync(["show", "-p", "SubState", "--value", "thermi.service"], cancel);

        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, hidden, UnderTheBar, cancel));
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, before, shownFirst, UnderTheBar, cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, shownFirst, shown, UnderTheBar, cancel));
        Assert.Equal(0, monitor.Exit);
        Assert.Equal(0, running.ExitCode);
        Assert.Equal("dead", exited);

        // Killed, the unit starts it again, and one overlay runs.
        var again = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await again;
        var restarted = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        await units.SystemctlAsync(["kill", "--signal=KILL", "thermi.service"], cancel);
        await restarted;
        Assert.Single(await units.ProcessesAsync(cancel));

        // The settings window's "Exit thermi" ends it as the menu's does, through the overlay's quit on the session's bus.
        var quitting = await units.StartWaitingAsync(cancel);
        string[] quit =
            ["call", "--session", "--dest", ShellLink.Name, "--object-path", ShellLink.ObjectPath, "--method", "org.gtk.Actions.Activate", "quit", "[]", "{}"];
        Assert.Equal(0, (await desktop.Session.RunAsync("gdbus", quit, cancel: cancel)).Exit);
        await quitting.WaitForExitAsync(cancel);
        Assert.Equal(0, quitting.ExitCode);
        Assert.Equal("dead", await units.SystemctlAsync(["show", "-p", "SubState", "--value", "thermi.service"], cancel));
    }

    // Weston offers neither the extension nor layer surfaces: thermi shows nothing, says why in one notice, and not again
    // the next time it starts.
    private static async Task NeitherSurfaceShowsAsync(TestDesktop desktop, CancellationToken cancel)
    {
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        for (var start = 0; start < 2; start++)
        {
            var thermi = await desktop.StartThermiAsync([], cancel);
            await thermi.WaitForExitAsync(cancel);
            Assert.NotEqual(0, thermi.ExitCode);
        }

        await notices.SyncAsync(cancel);
        Assert.Equal(1, notices.Count("no-overlay"));
    }

    private static async Task AnotherVersionShowsNothingAsync(string other, CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], other, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var update = notices.NextAsync("update-extension", cancel);
        var thermi = await desktop.StartThermiAsync([], cancel);

        var told = await update;
        var after = await desktop.ScreenshotAsync(cancel);
        // Its button is heard after the extension's Ready: thermi that read it still runs and asks GNOME for the update.
        var checking = notices.NextUpdateCheckAsync(cancel);
        await notices.ClickAsync(told, "update-extension", cancel);
        var asked = await Task.WhenAny(checking, thermi.WaitForExitAsync(cancel)) == checking;
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, after, UnderTheBar, cancel));
        Assert.True(asked, thermi.Error.Text);
    }

    // With no extension, thermi asks the Shell to install its own from extensions.gnome.org, which GNOME confirms itself.
    private static async Task NoneIsInstalledFromTheStoreAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        var asked = notices.NextInstallAsync(cancel);
        await desktop.StartThermiAsync([], cancel);

        var uuid = (string)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(TestShell.Thermi, "metadata.json"), cancel))!["uuid"]!;
        Assert.Equal(uuid, await asked);
    }

    // A right-click on the first overlay's first pill, which sits at its left, where every overlay starts; the tree is read once a menu shows.
    private static async Task<(string Program, int Entries)> OpenMenuAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel, button: 3);
        return Assert.Single(await Accessible.OpenMenusAsync(desktop.Session, cancel));
    }

    private static async Task ChooseAsync(TestDesktop desktop, IReadOnlyList<MenuRow> rows, MenuEntry entry, CancellationToken cancel)
    {
        await OpenMenuAsync(desktop, cancel);
        await Accessible.ChooseAsync(desktop, IndexOf(rows, entry), cancel);
    }

    private static int IndexOf(IReadOnlyList<MenuRow> rows, MenuEntry entry) => rows.ToList().FindIndex(row => row.Entry == entry);
}
