using Microsoft.Extensions.Logging.Abstractions;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Desktop;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Tests.Desktops.X11;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// What a person does to the overlay with the pointer on Linux (<c>docs/specs/006-linux/spec.md</c>, R11 and R23): drags
/// and snaps, across restarts and monitor changes, and the desktop gestures of spec 002.
/// </summary>
public sealed partial class GestureTests
{
    /// <summary>The tests' own program, started in a desktop's session with it, names the window's map's monitors (<see cref="SayWindowMap"/>).</summary>
    public const string WindowMap = "--window-map";

    // The first is the primary monitor, where the top bar or the panel is.
    private static readonly TestMonitor[] TwoMonitors = [new(1920, 1080), new(1920, 1080)];

    // Where the third overlay of the gestures sits, fading near the pointer, away from where every gesture drags.
    private static readonly Position Fading = new(1300, 200);

    // Where no overlay is, nor near enough for the fading one to fade.
    private static readonly Position Away = new(960, 900);

    public enum Desktop
    {
        Gnome,
        KWin,
        X11,

        /// <summary>GNOME, with the overlay of a person's own settings and the gestures they reported doing nothing.</summary>
        PersonsGnome,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Desktop.Gnome)]
    [InlineData(Desktop.KWin)]
    [InlineData(Desktop.X11)]
    public async Task TheOverlayIsDraggedAndStaysOnGnome(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(KindOf(row), TwoMonitors, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        var right = TwoMonitors[0].Width + TwoMonitors[1].Width;
        if (row == Desktop.X11)
        {
            await ResizePointerAsync(desktop, thermi, (Scene.OnWindow.X + width - 2, Scene.OnWindow.Y + height - 2), cancel);
        }

        // Into the second monitor's bottom right corner, then by its middle onto the primary monitor's top bar or panel.
        await DragAsync(desktop, thermi, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), (right - 4, TwoMonitors[1].Height - 4), cancel);
        var cornered = (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0];
        var mapped = await desktop.Session.RunAsync(Built.Program("Thermi.Linux.Tests"), [WindowMap], cancel: cancel);
        await DragAsync(desktop, thermi, (right - (width / 2) - 8, TwoMonitors[1].Height - (height / 2) - 8), (900, 4), cancel);
        var onPanel = (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0];
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        await desktop.SwapMonitorsAsync(TwoMonitors, cancel);
        var swapped = await desktop.ScreenshotAsync(cancel);
        await desktop.ShowThermiAsync([], cancel);
        var after = await desktop.ScreenshotAsync(cancel);

        Assert.Equal(SnapSpot.BottomRight, cornered.Snap?.Spot);
        Assert.NotNull(cornered.Snap!.Monitor);
        Assert.Null(cornered.Position);
        // Off GNOME's extension the settings window's map names the monitors as the overlay does, so a spot picked on it is
        // the overlay's: on GNOME the map waits on the Shell, which a test's program does not.
        if (row != Desktop.Gnome)
        {
            Assert.Contains(cornered.Snap.Monitor, mapped.Out.Split('\n'));
        }

        Assert.Equal(SnapSpot.Taskbar, onPanel.Snap?.Spot);
        Assert.Null(onPanel.Snap!.Monitor);
        Assert.NotNull(onPanel.Snap.Offset);
        Assert.Null(onPanel.Position);
        // The primary monitor is now on the right, its bar with it: the overlay shows there, and the left one shows none.
        var bar = (int)(height * 2.5);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, swapped, after, (TwoMonitors[1].Width, 0, TwoMonitors[0].Width, bar), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, swapped, after, (0, 0, TwoMonitors[1].Width, TwoMonitors[1].Height), cancel));
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Desktop.Gnome)]
    [InlineData(Desktop.KWin)]
    [InlineData(Desktop.X11)]
    [InlineData(Desktop.PersonsGnome)]
    public async Task DesktopGesturesWorkOnLinux(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        if (row == Desktop.PersonsGnome)
        {
            await PersonsOwnAsync(cancel);
            return;
        }

        await using var desktop = await TestDesktop.StartAsync(KindOf(row), [TwoMonitors[0]], cancel);
        var first = Scene.PanelOverlay;
        var second = Scene.Overlay with { Position = Scene.OnWindow };
        var fading = Scene.Overlay with { Position = Fading, Fade = true };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [first, second, fading] }, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (_, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        var top = row == Desktop.Gnome ? await Scene.PanelTopAsync(desktop, height, cancel) : Scene.OnPanel.Y;
        // On the first pill's first value, past its label, where a hold takes the value alone.
        var value = (Scene.OnPanel.X + 50, top + (height / 2));

        // A value lifted and dropped on the other overlay; the last pill lifted and dropped on the desktop.
        await HoldAndDropAsync(desktop, thermi, value, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), cancel);
        var joined = await Settings.ReadAsync(desktop.Session, cancel);
        // Esc on a lifted value, before the last pill leaves for an overlay of its own: at four overlays, the most, a hold lifts nothing.
        var (changed, kept) = await CancelledAsync(desktop, thermi, value, cancel);
        // The first overlay is shorter by the value it gave: its last pill ends where its picture ends now.
        var (shorter, _) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        // Into the monitor's bottom right corner, where it snaps.
        var corner = (TwoMonitors[0].Width - 4, TwoMonitors[0].Height - 4);
        await HoldAndDropAsync(desktop, thermi, (Scene.OnPanel.X + shorter - 16, value.Item2), corner, cancel);
        var separated = await Settings.ReadAsync(desktop.Session, cancel);
        // Control and the wheel, a turn at a time once thermi applied the last, as a person's turns come apart.
        for (var turn = 0; turn < 2; turn++)
        {
            var grew = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
            await desktop.Input.WheelAsync(value.Item1, value.Item2, 1, "Control_L", cancel);
            await grew;
        }

        var grown = await Settings.ReadAsync(desktop.Session, cancel);

        Assert.Equal(first.Items.Count - 1, joined.Overlays[0].Items.Count);
        Assert.Equal(second.Items.Count + 1, joined.Overlays[1].Items.Count);
        Assert.Equal(4, separated.Overlays.Count);
        Assert.NotNull(separated.Overlays[3].Snap);
        Assert.Equal(0, changed);
        Assert.Equal(ThermiConfigJson.Write(joined), ThermiConfigJson.Write(kept));
        Assert.Equal(first.Size + (2 * OverlaySizes.Step), grown.Overlays[0].Size, 6);
        await FadesAsync(desktop, thermi, row, 2 * value.Item2, cancel);
        if (row is Desktop.KWin or Desktop.X11)
        {
            await InFrontAsync(desktop, thermi, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), cancel);
            await StaysWholeAsync(desktop, thermi, row, cancel);
        }
    }

    /// <summary>
    /// The tests' own program as <see cref="TheOverlayIsDraggedAndStaysOnGnome"/> starts it in a desktop's session: the ids of
    /// the monitors the settings window's map draws without GNOME's extension, one a line.
    /// </summary>
    public static int SayWindowMap()
    {
        DesktopSession.Paints();
        Gtk.Module.Initialize();
        if (!Gtk.Functions.InitCheck())
        {
            return 1;
        }

        foreach (var screen in new WindowScreens(null, null, _ => { }, () => { }, NullLogger.Instance).Screens)
        {
            Console.WriteLine(screen.Id);
        }

        return 0;
    }

    // On a floating overlay's corner the pointer shows the resize arrows, as on Windows, and keeps them while the press drags
    // the corner, until Esc takes the drag back; X11 tells a test the pointer the screen shows. Each read waits for
    // thermi's next second, by which it took the pointer's last move.
    private static async Task ResizePointerAsync(TestDesktop desktop, SessionProcess thermi, (int X, int Y) corner, CancellationToken cancel)
    {
        async Task<string> ShownAsync(CancellationToken token)
        {
            await Traced.NextAsync(thermi.Error, Traced.Paint, token);
            return await XServers.PointerAsync(desktop.Session, token);
        }

        await desktop.Input.MoveAsync(corner.X - 40, corner.Y - 40, cancel);
        var away = await ShownAsync(cancel);
        await desktop.Input.MoveAsync(corner.X, corner.Y, cancel);
        var over = await ShownAsync(cancel);
        var held = "";
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await desktop.Input.HoldAsync(
            corner,
            (corner.X + 30, corner.Y),
            _ => Task.CompletedTask,
            async there =>
            {
                held = await ShownAsync(there);
                await desktop.Input.PressAsync("Escape", there);
                await dropped.WaitAsync(there);
            },
            cancel);

        Assert.NotEqual(away, over);
        Assert.Equal(over, held);
    }

    // Behind another program's window, a double-click on an overlay opens the settings window in front of it, with the
    // token the desktop gave the click, as GNOME's does in the settings window's own test.
    private static async Task InFrontAsync(TestDesktop desktop, SessionProcess thermi, (int X, int Y) on, CancellationToken cancel)
    {
        _ = await TestWindow.OpenAsync(desktop.Session, fullScreen: false, cancel);
        await Accessible.ActiveAsync(desktop.Session, TestWindow.Title, cancel);
        var shown = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await desktop.Input.DoubleClickAsync(on.X, on.Y, cancel);
        await shown;
        await Accessible.ActiveAsync(desktop.Session, "thermi", cancel);
    }

    // A drag by the pointer, pressed, moved and let go, once thermi says what it left is saved and on screen.
    private static async Task DragAsync(TestDesktop desktop, SessionProcess thermi, (int X, int Y) from, (int X, int Y) to, CancellationToken cancel)
    {
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await desktop.Input.DragAsync(from, to, cancel);
        await dropped;
    }

    // A hold until thermi lifts what is under the pointer, then a drop, once thermi says what it left is saved and on screen.
    private static async Task HoldAndDropAsync(TestDesktop desktop, SessionProcess thermi, (int X, int Y) from, (int X, int Y) to, CancellationToken cancel)
    {
        var lifted = Traced.NextAsync(thermi.Error, Traced.Lifted, cancel);
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await desktop.Input.HoldAndDropAsync(from, to, lifted.WaitAsync, cancel);
        await dropped;
    }

    // A drag that Esc cancels while the value is lifted and dragged over the other overlay puts everything back: what it
    // changed on screen, and the settings after. The pointer leaves the overlays before the button goes up, as the other
    // overlay's value it rests on would show its tooltip once the press is over, as Windows' does, where the screen before
    // had none, its drop's quiet holding it.
    private static async Task<(long Changed, ThermiConfig Kept)> CancelledAsync(
        TestDesktop desktop, SessionProcess thermi, (int X, int Y) value, CancellationToken cancel)
    {
        var screen = await desktop.ScreenshotAsync(cancel);
        var lifted = Traced.NextAsync(thermi.Error, Traced.Lifted, cancel);
        var dropped = Traced.NextAsync(thermi.Error, Traced.Dropped, cancel);
        await desktop.Input.HoldAsync(
            value,
            (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8),
            lifted.WaitAsync,
            async there =>
            {
                await desktop.Input.PressAsync("Escape", there);
                await dropped.WaitAsync(there);
                await desktop.Input.MoveAsync(Away.X, Away.Y, there);
            },
            cancel);
        var cancelled = await desktop.ScreenshotAsync(cancel);
        var size = await Pictures.SizeAsync(desktop.Session, screen, cancel);
        var changed = await ChangedButTheClockAsync(desktop, screen, cancelled, size, 2 * value.Y, cancel);
        return (changed, await Settings.ReadAsync(desktop.Session, cancel));
    }

    // The pixels two screenshots differ by from the top of the screen down to height, but for the middle third of the
    // strip the first overlay sits in, from the top down to strip: GNOME's clock is there, and turns its minute whenever
    // it will, as between two screenshots once in sixty runs or so.
    private static async Task<long> ChangedButTheClockAsync(
        TestDesktop desktop, string before, string after, (int Width, int Height) size, int strip, CancellationToken cancel)
    {
        var (width, height) = size;
        var third = width / 3;
        (int, int, int, int)[] parts = [(0, 0, third, strip), (width - third, 0, third, strip), (0, strip, width, height - strip)];
        long changed = 0;
        foreach (var part in parts)
        {
            changed += await Pictures.ChangedAsync(desktop.Session, before, after, part, cancel);
        }

        return changed;
    }

    // Fade near the pointer: GNOME's extension and X11 know where the pointer is anywhere, KWin tells thermi only over its
    // surface. What fades is the overlay itself: the rest of the screen stays as it was, the bar's clock aside.
    private static async Task FadesAsync(TestDesktop desktop, SessionProcess thermi, Desktop row, int strip, CancellationToken cancel)
    {
        var shown = await desktop.ScreenshotAsync(cancel);
        var (x, y) = Near(row);
        var faded = Traced.NextAsync(thermi.Error, Traced.Faded, cancel);
        await desktop.Input.MoveAsync(x, y, cancel);
        await faded;
        var screen = await desktop.ScreenshotAsync(cancel);
        var (width, _) = await Pictures.SizeAsync(desktop.Session, shown, cancel);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, screen, (Fading.X, Fading.Y, 200, 40), cancel));
        Assert.Equal(0, await ChangedButTheClockAsync(desktop, shown, screen, (width, Fading.Y - 50), strip, cancel));
    }

    // While the settings window is open no overlay fades, as on Windows, so what the person edits shows whole: with the
    // overlay whole again, the pointer away, the pointer back near it fades none in thermi's next two seconds.
    private static async Task StaysWholeAsync(TestDesktop desktop, SessionProcess thermi, Desktop row, CancellationToken cancel)
    {
        var (x, y) = Near(row);
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        var before = Fadings(thermi);
        await desktop.Input.MoveAsync(x, y, cancel);
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: 2);
        Assert.Contains(await Accessible.WindowsAsync(desktop.Session, cancel), shown => shown.Program == "thermi");
        Assert.Equal(before, Fadings(thermi));
    }

    private static (int X, int Y) Near(Desktop row) => row != Desktop.KWin ? (Fading.X - 40, Fading.Y - 40) : (Fading.X + 8, Fading.Y + 8);

    private static int Fadings(SessionProcess thermi) => thermi.Error.Text.Split('\n').Count(line => Traced.Of(line)?.Event == Traced.Faded);

    private static DesktopKind KindOf(Desktop row) => row switch
    {
        Desktop.Gnome or Desktop.PersonsGnome => DesktopKind.Gnome,
        Desktop.KWin => DesktopKind.KWin,
        Desktop.X11 => DesktopKind.X11,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown desktop."),
    };
}
