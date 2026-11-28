using System.Diagnostics;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.MacOS.Tests.Desktop;
using Screen = Thermi.MacOS.Tests.Desktop.Screen;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// The overlay on the Mac (<c>docs/specs/008-macos/spec.md</c>, R9 to R14): above every window, the Dock and full-screen
/// apps as <c>thermi preview</c> paints it, clicks through where it draws nothing, its snaps, its menu and its agent, and
/// the desktop gestures of spec 002.
/// </summary>
public sealed class OverlayTests
{
    public enum Shown
    {
        Retina,
        NotRetina,
        OtherAppFocused,
        FullScreen,
        SensorsStopped,
        HiddenInFullScreen,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Shown.Retina, Skip = "Waiting for its code")]
    [InlineData(Shown.NotRetina, Skip = "Waiting for its code")]
    [InlineData(Shown.OtherAppFocused, Skip = "Waiting for its code")]
    [InlineData(Shown.FullScreen, Skip = "Waiting for its code")]
    [InlineData(Shown.SensorsStopped, Skip = "Waiting for its code")]
    [InlineData(Shown.HiddenInFullScreen, Skip = "Waiting for its code")]
    public async Task TheOverlayShowsAboveEveryWindowOnMacOS(Shown row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var recording = row == Shown.SensorsStopped ? null : SensorFeed.MacMini;
        await using var mac = await TestMac.StartAsync(recording, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0];
        // A monitor is Retina or not as the Mac has it: the row of the other kind has nothing to show on.
        Assert.SkipWhen(row == Shown.Retina && main.Scale < 2, "The main monitor is not Retina.");
        Assert.SkipWhen(row == Shown.NotRetina && main.Scale >= 2, "The main monitor is Retina.");
        var full = row is Shown.FullScreen or Shown.HiddenInFullScreen;
        var at = new Position(main.Visible.X + 200, main.Visible.Y + 200);
        var visibility = row == Shown.HiddenInFullScreen ? Visibility.HideInFullscreen : Visibility.Always;
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = at, Visibility = visibility }] }, cancel);
        var window = await Screen.ShowWindowAsync(mac.Session, (main.Visible.X, main.Visible.Y, 800, 600), full, cancel);

        var picture = await mac.PreviewAsync(recording, main.Scale, cancel);
        var area = (at.X, at.Y, (int)(picture.Width / main.Scale), (int)(picture.Height / main.Scale));
        var before = await Screen.CaptureAsync(mac.Session, area, cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);
        await TestMac.PaintedAsync(thermi, cancel);
        var screen = await Screen.CaptureAsync(mac.Session, area, cancel);

        if (row == Shown.HiddenInFullScreen)
        {
            Assert.Equal(0, screen.Differing(before));
            return;
        }

        Assert.Equal(0, screen.Differing(picture));
        // Where it draws nothing, the click is the window's, which keeps the focus; and thermi is an agent app.
        var (x, y) = picture.Nothing();
        var clicked = window.Error.NextAsync(line => line == "click", cancel);
        await Input.ClickAsync(mac.Session, at.X + (int)(x / main.Scale), at.Y + (int)(y / main.Scale), cancel);
        await clicked;
        Assert.Equal(window.Id, await Screen.FrontAsync(mac.Session, cancel));
        Assert.Equal("UIElement", await Screen.AppKindAsync(mac.Session, thermi.Id, cancel));
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheOverlayIsDraggedAndStaysOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var monitors = await mac.MonitorsAsync(cancel);
        Assert.SkipWhen(monitors.Count < 2, "The Mac has one monitor.");
        await mac.DockAsync("bottom", hides: false, cancel);
        var main = monitors[0].Visible;
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = new(main.X + 300, main.Y + 300) }] }, cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);

        // Let go past the visible frame's top right corner: it snaps inside it, beside the menu bar.
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await Input.DragAsync(mac.Session, (main.X + 308, main.Y + 308), (main.X + main.Width + 40, main.Y - 10), cancel);
        await dropped;
        var snapped = (await mac.ReadSettingsAsync(cancel)).Overlays[0];
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        thermi = await mac.ShowThermiAsync([], cancel);
        var picture = await mac.PreviewAsync(SensorFeed.MacMini, monitors[0].Scale, cancel);
        var (width, height) = ((int)(picture.Width / monitors[0].Scale), (int)(picture.Height / monitors[0].Scale));
        var restarted = await Screen.CaptureAsync(mac.Session, (main.X + main.Width - width, main.Y, width, height), cancel);

        Assert.Equal(SnapSpot.TopRight, snapped.Snap?.Spot);
        Assert.Null(snapped.Position);
        Assert.Equal(0, restarted.Differing(picture));
        await StaysInTheCornerAsync(mac, thermi, picture, (width, height), cancel);
    }

    // The Dock on the left, then hidden, then the monitors swapped: the snap's corner moves with the main monitor's visible
    // frame, and stays put.
    private static async Task StaysInTheCornerAsync(
        TestMac mac, SessionProcess thermi, Picture picture, (int Width, int Height) size, CancellationToken cancel)
    {
        Func<Task>[] changes = [() => mac.DockAsync("left", false, cancel), () => mac.DockAsync("left", true, cancel), () => mac.SwapMonitorsAsync(cancel)];
        foreach (var change in changes)
        {
            var painted = TestMac.PaintedAsync(thermi, cancel);
            await change();
            await painted;
            var moved = (await mac.MonitorsAsync(cancel))[0].Visible;
            var there = await Screen.CaptureAsync(mac.Session, (moved.X + moved.Width - size.Width, moved.Y, size.Width, size.Height), cancel);
            Assert.Equal(0, there.Differing(picture));
        }
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheOverlaysMenuIsTheMacsOwn()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0].Visible;
        var pill = (X: main.X + 208, Y: main.Y + 208);
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = new(main.X + 200, main.Y + 200) }] }, cancel);
        var registered = await mac.Session.RunAsync(Built.Thermi, ["register"], cancel: cancel);
        var pid = await Launchd.PidAsync(mac.Session, Launchd.OverlayLabel, 0, cancel);
        var entries = Menus.OfOverlay(ThermiConfig.New, 0, new MenuState(false, false, ServiceState.Running, null, [], false)).ToList();

        // A right-click and a Control-click open AppKit's own menu, with Core's entries; Esc closes it.
        await Input.MenuClickAsync(mac.Session, pill.X, pill.Y, control: false, cancel);
        var opened = await MenuReader.EntriesAsync(mac.Session, pid, cancel);
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        await Input.MenuClickAsync(mac.Session, pill.X, pill.Y, control: true, cancel);
        var controlOpened = await MenuReader.EntriesAsync(mac.Session, pid, cancel);
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        var closed = await MenuReader.EntriesAsync(mac.Session, pid, cancel);
        var (before, hidden, back, monitor) = await HiddenAndShownAsync(mac, pid, pill, entries, cancel);
        // Exit thermi ends it with 0, and launchd leaves it ended.
        using (var running = Process.GetProcessById(pid))
        {
            await ChooseAsync(mac, pid, pill, entries, MenuEntry.Exit, cancel);
            await running.WaitForExitAsync(cancel).WaitAsync(Bound.End, TimeProvider.System, cancel);
        }

        var (gui, _) = await mac.Session.DomainsAsync(cancel);
        var exited = await Launchd.PrintAsync(mac.Session, gui, Launchd.OverlayLabel, cancel);

        Assert.Equal(0, registered.Exit);
        Assert.Equal(entries.Count, opened.Count);
        Assert.Equal(opened, controlOpened);
        Assert.Empty(closed);
        Assert.NotEqual(0, hidden.Differing(before));
        Assert.Equal(0, back.Differing(before));
        Assert.True(monitor, "Activity Monitor did not open.");
        Assert.Contains("last exit code = 0", exited, StringComparison.Ordinal);
        Assert.DoesNotContain("pid = ", exited, StringComparison.Ordinal);
        await RestartsWhenKilledAsync(mac, gui, cancel);
    }

    // Hide overlays from the overlay's menu, and back from thermi's own; then Task Manager, which opens Activity Monitor.
    private static async Task<(Picture Before, Picture Hidden, Picture Back, bool Monitor)> HiddenAndShownAsync(
        TestMac mac, int pid, (int X, int Y) pill, List<MenuRow> entries, CancellationToken cancel)
    {
        var thermis = Menus.Thermis(ThermiConfig.New, new MenuState(true, false, ServiceState.Running, null, [], false)).ToList();
        var around = (pill.X - 8, pill.Y - 8, 200, 40);
        var before = await Screen.CaptureAsync(mac.Session, around, cancel);
        await ChooseAsync(mac, pid, pill, entries, MenuEntry.HideOverlays, cancel);
        var hidden = await Screen.CaptureAsync(mac.Session, around, cancel);
        await MenuReader.OpenStatusItemAsync(mac.Session, pid, cancel);
        await MenuReader.ChooseAsync(mac.Session, pid, thermis.FindIndex(row => row.Entry == MenuEntry.ShowOverlays), cancel);
        var back = await Screen.CaptureAsync(mac.Session, around, cancel);
        await ChooseAsync(mac, pid, pill, entries, MenuEntry.TaskManager, cancel);
        return (before, hidden, back, await Screen.RunsAsync(mac.Session, "com.apple.ActivityMonitor", cancel));
    }

    // Killed, as a crash ends it, launchd starts the overlay's agent again, and one overlay shows.
    private static async Task RestartsWhenKilledAsync(TestMac mac, string gui, CancellationToken cancel)
    {
        await mac.Session.RunAsync("launchctl", ["kickstart", $"{gui}/{Launchd.OverlayLabel}"], cancel: cancel);
        var started = await Launchd.PidAsync(mac.Session, Launchd.OverlayLabel, 0, cancel);
        await mac.Session.RunAsync("kill", ["-9", $"{started}"], cancel: cancel);
        var again = await Launchd.PidAsync(mac.Session, Launchd.OverlayLabel, started, cancel);

        Assert.NotEqual(started, again);
        Assert.Single((await mac.Session.RunAsync("pgrep", ["-x", "thermi"], cancel: cancel)).Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task DesktopGesturesWorkOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0].Visible;
        var first = OverlayConfig.First with { Position = new(main.X + 100, main.Y + 100) };
        var second = OverlayConfig.First with { Position = new(main.X + 100, main.Y + 400), Fade = true };
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [first, second] }, cancel);
        var thermi = await mac.ShowThermiAsync([], cancel);
        var value = (X: main.X + 108, Y: main.Y + 108);
        var onSecond = (X: main.X + 108, Y: main.Y + 408);

        // A value held, lifted and dropped on the other overlay; then on the desktop, where it makes an overlay of its own.
        var joined = await HoldAndDropAsync(mac, thermi, value, onSecond, cancel);
        var separated = await HoldAndDropAsync(mac, thermi, value, (main.X + 700, main.Y + 500), cancel);
        var undone = await UndoAsync(mac, thermi, value, cancel);
        var kept = await CancelledAsync(mac, thermi, value, onSecond, cancel);
        // ⌘ and the wheel, then a pinch, each a step of the overlay's size.
        var grown = await SizedAsync(mac, thermi, () => Input.CommandWheelAsync(mac.Session, value.X, value.Y, 1, cancel), cancel);
        var pinched = await SizedAsync(mac, thermi, () => Input.PinchAsync(mac.Session, value.X, value.Y, 0.2, cancel), cancel);
        // Near the pointer the second overlay fades, and still takes a double-click, which opens the window.
        var faded = Traced.NextAsync(thermi.Error, Traced.Faded, cancel);
        await Input.MoveAsync(mac.Session, onSecond.X - 30, onSecond.Y - 30, cancel);
        await faded;
        var window = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await Input.ClickAsync(mac.Session, onSecond.X, onSecond.Y, cancel, times: 2);

        Assert.Equal(first.Items.Count - 1, joined.Overlays[0].Items.Count);
        Assert.Equal(second.Items.Count + 1, joined.Overlays[1].Items.Count);
        Assert.Equal(3, separated.Overlays.Count);
        Assert.Equal(ThermiConfigJson.Write(joined), ThermiConfigJson.Write(undone));
        Assert.Equal(ThermiConfigJson.Write(undone), ThermiConfigJson.Write(kept));
        Assert.Equal(1 + OverlaySizes.Step, grown.Overlays[0].Size, 6);
        Assert.Equal(1 + (2 * OverlaySizes.Step), pinched.Overlays[0].Size, 6);
        Assert.NotNull(await window);
    }

    private static async Task<ThermiConfig> HoldAndDropAsync(
        TestMac mac, SessionProcess thermi, (int X, int Y) from, (int X, int Y) to, CancellationToken cancel)
    {
        var lifted = Traced.NextAsync(thermi.Error, Traced.Lifted, cancel);
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await Input.DragAsync(mac.Session, from, from, cancel, letGo: false);
        await lifted;
        await Input.DragAsync(mac.Session, from, to, cancel);
        await dropped;
        return await mac.ReadSettingsAsync(cancel);
    }

    // Esc while lifted puts it all back, and reaches no other app: the key never leaves thermi.
    private static async Task<ThermiConfig> CancelledAsync(TestMac mac, SessionProcess thermi, (int X, int Y) from, (int X, int Y) to, CancellationToken cancel)
    {
        var lifted = Traced.NextAsync(thermi.Error, Traced.Lifted, cancel);
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await Input.DragAsync(mac.Session, from, to, cancel, letGo: false);
        await lifted;
        await Input.KeyAsync(mac.Session, Input.Escape, cancel);
        await dropped;
        await Input.LetGoAsync(mac.Session, to.X, to.Y, cancel);
        return await mac.ReadSettingsAsync(cancel);
    }

    // The overlay's menu offers to undo the last gesture for a while after it.
    private static async Task<ThermiConfig> UndoAsync(TestMac mac, SessionProcess thermi, (int X, int Y) pill, CancellationToken cancel)
    {
        var config = await mac.ReadSettingsAsync(cancel);
        var state = new MenuState(false, false, ServiceState.Running, TimeSpan.Zero, [], false);
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await ChooseAsync(mac, thermi.Id, pill, [.. Menus.OfOverlay(config, 0, state)], MenuEntry.UndoGesture, cancel);
        await applied;
        return await mac.ReadSettingsAsync(cancel);
    }

    private static async Task<ThermiConfig> SizedAsync(TestMac mac, SessionProcess thermi, Func<Task> gesture, CancellationToken cancel)
    {
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await gesture();
        await applied;
        return await mac.ReadSettingsAsync(cancel);
    }

    private static async Task ChooseAsync(TestMac mac, int pid, (int X, int Y) pill, List<MenuRow> entries, MenuEntry entry, CancellationToken cancel)
    {
        await Input.MenuClickAsync(mac.Session, pill.X, pill.Y, control: false, cancel);
        await MenuReader.ChooseAsync(mac.Session, pid, entries.FindIndex(row => row.Entry == entry), cancel);
    }
}
