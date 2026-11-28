using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Tests.Desktops.X11;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// The overlay on each Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R8 to R13, R17 and R24): above every window
/// and panel as <c>thermi preview</c> paints it, clicks through where it draws nothing, its menu, and its unit.
/// </summary>
public sealed partial class OverlayTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    private static readonly (int X, int Y, int Width, int Height) UnderTheBar = (Scene.OnWindow.X, Scene.OnWindow.Y, 400, 200);

    // Where an overlay dragged on the window is let go: below the other overlay, and away from every snap's reach.
    private static readonly Position Free = new(400, 750);

    public enum GnomeCase
    {
        WaylandAt150,
        X11,
        FullScreen,
        SensorsStopped,
        HiddenInFullScreen,
    }

    public enum LayerCase
    {
        KWin,
        SwayAt150,
        FullScreen,
        Neither,
        KWinBottomPanel,
        SwayLeftPanel,
    }

    public enum X11Case
    {
        BottomPanel,
        LeftPanelAt200,
    }

    public enum MenuCase
    {
        Gnome,
        KWin,
        Xfce,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(GnomeCase.WaylandAt150)]
    [InlineData(GnomeCase.X11)]
    [InlineData(GnomeCase.FullScreen)]
    [InlineData(GnomeCase.SensorsStopped)]
    [InlineData(GnomeCase.HiddenInFullScreen)]
    public async Task TheOverlayShowsAboveEveryWindowOnGnome(GnomeCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var scale = row == GnomeCase.WaylandAt150 ? 1.5 : 1;
        TestMonitor[] monitors = row == GnomeCase.HiddenInFullScreen ? [Monitor, Monitor] : [Monitor with { Scale = scale }];
        await using var desktop = await TestDesktop.StartAsync(row == GnomeCase.X11 ? DesktopKind.GnomeX11 : DesktopKind.Gnome, monitors, cancel);
        if (row == GnomeCase.HiddenInFullScreen)
        {
            await HiddenInFullScreenAsync(desktop, cancel);
            return;
        }

        var scene = await Scene.ShowAsync(desktop, scale, row == GnomeCase.FullScreen, row == GnomeCase.SensorsStopped, cancel);

        // Shown always, as it is by default, the overlay shows over a full-screen window too, where the top bar does not.
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        if (row != GnomeCase.FullScreen)
        {
            Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
            Assert.Equal("click 1", await scene.ClickThroughAsync(cancel));
        }
    }

    // "Hide in full screen" hides an overlay where it shows: the second monitor, where the pointer opens the window full
    // screen, though its corner lies on the first, as an overlay that grew there keeps it.
    private static async Task HiddenInFullScreenAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.Input.MoveAsync(Monitor.Width + (Monitor.Width / 2), Monitor.Height / 2, cancel);
        var across = Scene.Overlay with { Position = new Position(Monitor.Width - 40, Scene.OnWindow.Y), Visibility = Visibility.HideInFullscreen };
        var scene = await Scene.ShowAsync(desktop, 1, fullScreen: true, stopped: false, cancel, first: across);

        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, scene.Before, scene.Screen, (Monitor.Width, 0, Monitor.Width, Monitor.Height), cancel));
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(LayerCase.KWin)]
    [InlineData(LayerCase.SwayAt150)]
    [InlineData(LayerCase.FullScreen)]
    [InlineData(LayerCase.Neither)]
    [InlineData(LayerCase.KWinBottomPanel)]
    [InlineData(LayerCase.SwayLeftPanel)]
    public async Task TheOverlayShowsAboveEveryWindowOnLayerShell(LayerCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var scale = row == LayerCase.SwayAt150 ? 1.5 : 1;
        var (kind, edge) = row switch
        {
            LayerCase.SwayAt150 => (DesktopKind.Sway, PanelEdge.Top),
            LayerCase.SwayLeftPanel => (DesktopKind.Sway, PanelEdge.Left),
            LayerCase.Neither => (DesktopKind.BareWayland, PanelEdge.Top),
            LayerCase.KWin or LayerCase.FullScreen => (DesktopKind.KWin, PanelEdge.Top),
            LayerCase.KWinBottomPanel => (DesktopKind.KWin, PanelEdge.Bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(kind, [Monitor with { Scale = scale }], cancel, edge);
        if (row == LayerCase.Neither)
        {
            await NeitherSurfaceShowsAsync(desktop, cancel);
            return;
        }

        var scene = await Scene.ShowAsync(desktop, scale, fullScreen: false, stopped: false, cancel, first: BesideSide(edge));
        var (_, height) = await Pictures.SizeAsync(desktop.Session, scene.Picture, cancel);
        var onPanel = edge == PanelEdge.Top ? Scene.OnPanel : OnPanelOf(edge, scale, height, TestPanel.Height);
        Assert.Equal(0, await scene.DifferingAtAsync(onPanel, cancel));
        Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
        Assert.Equal("click 1", await scene.ClickThroughAsync(cancel));
        if (row == LayerCase.FullScreen)
        {
            // A window that goes full screen in front of the overlay covers it, as every window of a layer below the top one.
            await desktop.OpenWindowAsync(fullScreen: true, cancel);
            Assert.Equal(0, await CoveredAsync(scene, Scene.OnWindow, cancel));
        }
        else if (row == LayerCase.KWinBottomPanel)
        {
            // The menu of an overlay on a panel along the bottom opens above it, where it fits, as KWin keeps it where told,
            // and whole, every entry in it.
            await desktop.Input.ClickAsync(onPanel.X + 8, onPanel.Y + (height / 2), cancel, button: 3);
            _ = Assert.Single(await Accessible.OpenMenusAsync(desktop.Session, cancel));
            var opened = await desktop.ScreenshotAsync(cancel);
            Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, scene.Screen, opened, (onPanel.X, onPanel.Y - 110, 100, 100), cancel));
            Assert.Equal(0, await Accessible.CutEntriesAsync(desktop.Session, cancel));
            await desktop.Input.PressAsync("Escape", cancel);
            await Accessible.ClosedAsync(desktop.Session, cancel);
            await DroppedWhereLetGoAsync(scene, onPanel, height, cancel);
        }
    }

    // Dragged off the panel by its first pill, it stays where the pointer let it go, though the pointer stood still on it a
    // while as the overlay moved under it, as KWin tells no motion for that.
    private static async Task DroppedWhereLetGoAsync(Scene scene, Position onPanel, int height, CancellationToken cancel)
    {
        var dropped = Traced.NextAsync(scene.Thermi.Error, Traced.Dropped, cancel);
        await scene.Desktop.Input.HoldAsync(
            (onPanel.X + 16, onPanel.Y + (height / 2)),
            (Free.X + 16, Free.Y + (height / 2)),
            _ => Task.CompletedTask,
            there => Traced.NextAsync(scene.Thermi.Error, Traced.Paint, there),
            cancel);
        await dropped;
        Assert.Equal<Position?>(Free, (await Settings.ReadAsync(scene.Desktop.Session, cancel)).Overlays[0].Position);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(X11Case.BottomPanel)]
    [InlineData(X11Case.LeftPanelAt200)]
    public async Task TheOverlayShowsAboveEveryWindowOnX11(X11Case row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var (edge, scale) = row switch
        {
            X11Case.BottomPanel => (PanelEdge.Bottom, 1),
            X11Case.LeftPanelAt200 => (PanelEdge.Left, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [Monitor with { Scale = scale }], cancel, edge);
        var scene = await Scene.ShowAsync(desktop, scale, fullScreen: false, stopped: false, cancel, clickThrough: true, BesideSide(edge));

        var (width, height) = await Pictures.SizeAsync(desktop.Session, scene.Picture, cancel);
        var onPanel = OnPanelOf(edge, scale, height, XfceDesktop.PanelRoom);
        Assert.Equal(0, await scene.DifferingAtAsync(onPanel, cancel));
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
        Assert.Equal("click 1", await scene.ClickAsync(Scene.OnWindow.X + (width / 2), Scene.OnWindow.Y + (height / 2), cancel));
        // Dragged off the panel by its first pill, it shows and stays where the pointer let it go.
        var dropped = Traced.NextAsync(scene.Thermi.Error, Traced.Dropped, cancel);
        await desktop.Input.DragAsync((onPanel.X + 16, onPanel.Y + (height / 2)), (Free.X + 16, Free.Y + (height / 2)), cancel);
        await dropped;
        var moved = scene with { Screen = await desktop.ScreenshotAsync(cancel) };
        Assert.Equal<Position?>(Free, (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0].Position);
        Assert.Equal(0, await moved.DifferingAtAsync(Free, cancel));
        await desktop.OpenWindowAsync(fullScreen: true, cancel);
        Assert.Equal(0, await CoveredAsync(moved, Free, cancel));
    }

    // The first overlay snapped to the taskbar from the monitor's left edge, as far as it goes beside a side panel.
    private static OverlayConfig? BesideSide(PanelEdge edge) =>
        edge == PanelEdge.Left ? Scene.PanelOverlay with { Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = 0 } } : null;

    // Where Core's snaps put the first overlay, by a panel that keeps its room's pixels off the work area: across its middle
    // along the bottom; beside it on a side, at the bottom of the work area, a margin up. A panel at the top is the
    // layer rows' and GestureTests' X11 rows'.
    private static Position OnPanelOf(PanelEdge edge, double scale, int height, int room) => edge switch
    {
        PanelEdge.Bottom => new(Scene.OnPanel.X, Monitor.Height - room + ((room - height) / 2)),
        PanelEdge.Left => new(room, Monitor.Height - (int)Math.Round(Snaps.Margin * scale) - height),
        PanelEdge.Top or _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "No row has this edge."),
    };

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

    [Theory(Timeout = Bound.Test)]
    [InlineData(MenuCase.Gnome)]
    [InlineData(MenuCase.KWin)]
    [InlineData(MenuCase.Xfce)]
    public async Task TheOverlaysMenuIsTheShellsOwn(MenuCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        // Each desktop's kind, the program that shows the menu, the name its sign-in gives the desktop, and its system monitor.
        var (kind, shows, named, monitorProgram, monitorName) = row switch
        {
            MenuCase.Gnome => (DesktopKind.Gnome, "gnome-shell", "GNOME", "gnome-system-monitor", "org.gnome.SystemMonitor"),
            MenuCase.KWin => (DesktopKind.KWin, "thermi", "KDE", "plasma-systemmonitor", "org.kde.plasma-systemmonitor"),
            MenuCase.Xfce => (DesktopKind.X11, "thermi", "XFCE", "xfce4-taskmanager", "org.xfce.taskmanager"),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(kind, [Monitor], cancel);
        var units = await UserUnits.StartAsync(desktop.Session, cancel);
        await units.SystemctlAsync(["set-environment", $"XDG_CURRENT_DESKTOP={named}"], cancel);
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
        // Each shot of the overlay waits for thermi's next second after it showed, so it shows the screen settled.
        await Traced.NextAsync(units.Trace, Traced.Paint, cancel);
        var shownFirst = await desktop.ScreenshotAsync(cancel);
        var pill = config.Overlays[0].Items[0].Id;
        // The overlay shows the feed's readings, so its menu offers Copy as image, as Windows' does on a painted overlay.
        var rows = Menus.OfOverlay(config, 0, new MenuState(false, false, ServiceState.Running, null, [], false), pill);

        Assert.Equal((shows, rows.Count), await OpenMenuAsync(desktop, cancel));
        // Open, the menu takes the keys, and Esc closes it, as a click outside it does.
        await desktop.Input.PressAsync("Escape", cancel);
        await Accessible.ClosedAsync(desktop.Session, cancel);
        await OpenMenuAsync(desktop, cancel);
        await desktop.Input.ClickAsync(Monitor.Width - 100, Monitor.Height - 100, cancel);
        await Accessible.ClosedAsync(desktop.Session, cancel);
        await OpenMenuAsync(desktop, cancel);
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
        await Traced.NextAsync(units.Trace, Traced.Paint, cancel);
        // Back under the pointer, which rests where the menu was opened and never moved: a right-click there is on the pill.
        await units.AddProgramAsync(monitorProgram, monitorName, cancel);
        await ChooseAsync(desktop, rows, MenuEntry.TaskManager, cancel);
        var monitor = await desktop.Session.RunAsync("gdbus", ["wait", "--session", "--timeout", Bound.LineSeconds, monitorName], cancel: cancel);
        // With the pointer off it since the menu, it shows no value's tooltip, as when it was first drawn.
        await Traced.NextAsync(units.Trace, Traced.Paint, cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        await ChooseAsync(desktop, rows, MenuEntry.Exit, cancel);
        // systemctl --wait returns once the unit went inactive; not "auto-restart": a clean exit starts nothing again.
        await running.WaitForExitAsync(cancel);
        var exited = await units.SystemctlAsync(["show", "-p", "SubState", "--value", "thermi.service"], cancel);

        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, hidden, UnderTheBar, cancel));
        // Shown again, it is as first drawn; against the screen without it, each shot's count says which lacks it, if one does.
        var drawn = await Pictures.ChangedAsync(desktop.Session, before, shownFirst, UnderTheBar, cancel);
        Assert.NotEqual(0, drawn);
        Assert.Equal(
            (drawn, 0L),
            (await Pictures.ChangedAsync(desktop.Session, before, shown, UnderTheBar, cancel),
                await Pictures.ChangedAsync(desktop.Session, shownFirst, shown, UnderTheBar, cancel)));
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
    // the next time it starts. A start with no notices to say it on says nothing, so the next says it; one whose GTK cannot
    // start, with no display, is no desktop without surfaces, and says nothing of them.
    private static async Task NeitherSurfaceShowsAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        async Task EndsAsync(Dictionary<string, string>? variables)
        {
            var thermi = await desktop.Session.StartAsync(Built.Program("thermi"), [], variables, cancel);
            await thermi.WaitForExitAsync(cancel);
            Assert.NotEqual(0, thermi.ExitCode);
        }

        await EndsAsync(null);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await EndsAsync(new Dictionary<string, string> { ["WAYLAND_DISPLAY"] = "", ["DISPLAY"] = "" });
        await notices.SyncAsync(cancel);
        var noDisplay = notices.Count("no-overlay");
        for (var start = 0; start < 2; start++)
        {
            await EndsAsync(null);
        }

        await notices.SyncAsync(cancel);
        Assert.Equal((0, 1), (noDisplay, notices.Count("no-overlay")));
    }

    // A right-click on the first overlay's first pill, which sits at its left, where every overlay starts; the tree is read once a menu shows.
    private static async Task<(string Program, int Entries)> OpenMenuAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel, button: 3);
        return Assert.Single(await Accessible.OpenMenusAsync(desktop.Session, cancel));
    }

    private static async Task ChooseAsync(TestDesktop desktop, IReadOnlyList<MenuRow> rows, MenuEntry entry, CancellationToken cancel)
    {
        // The pill's menu, as counted first, or the entry's index is another's of the menu that opened.
        Assert.Equal(rows.Count, (await OpenMenuAsync(desktop, cancel)).Entries);
        // A person's pointer goes to the menu, off the pill, before a row is chosen. One that leaves only as the menu lets it
        // go can leave the overlay on X11 with a value under it that it never hears the pointer leave, whose tooltip shows.
        await desktop.Input.MoveAsync(Monitor.Width - 100, Monitor.Height - 100, cancel);
        await Accessible.ChooseAsync(desktop, IndexOf(rows, entry), cancel);
    }

    private static int IndexOf(IReadOnlyList<MenuRow> rows, MenuEntry entry) => rows.ToList().FindIndex(row => row.Entry == entry);
}
