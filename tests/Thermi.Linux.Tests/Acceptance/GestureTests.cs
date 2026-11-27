using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// What a person does to the overlay with the pointer on Linux (<c>docs/specs/006-linux/spec.md</c>, R11 and R23): drags
/// and snaps, across restarts and monitor changes, and the desktop gestures of spec 002.
/// </summary>
public sealed class GestureTests
{
    // The first is the primary monitor, where the top bar or the panel is.
    private static readonly TestMonitor[] TwoMonitors = [new(1920, 1080), new(1920, 1080)];

    // Where the third overlay of the gestures sits, fading near the pointer, away from where every gesture drags.
    private static readonly Position Fading = new(1300, 200);

    public enum Desktop
    {
        Gnome,
        KWin,
        X11,
    }

    [Theory]
    [InlineData(Desktop.Gnome)]
    [InlineData(Desktop.KWin, Skip = "Waiting for its code")]
    [InlineData(Desktop.X11, Skip = "Waiting for its code")]
    public async Task TheOverlayIsDraggedAndStaysOnGnome(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(KindOf(row), TwoMonitors, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        var right = TwoMonitors[0].Width + TwoMonitors[1].Width;

        // Into the second monitor's bottom right corner, then by its middle onto the primary monitor's top bar or panel.
        await DragAsync(desktop, thermi, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), (right - 4, TwoMonitors[1].Height - 4), cancel);
        var cornered = (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0];
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
        Assert.Equal(SnapSpot.Taskbar, onPanel.Snap?.Spot);
        Assert.Null(onPanel.Snap!.Monitor);
        Assert.NotNull(onPanel.Snap.Offset);
        Assert.Null(onPanel.Position);
        // The primary monitor is now on the right, its bar with it: the overlay shows there, and the left one shows none.
        var bar = (int)(height * 2.5);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, swapped, after, (TwoMonitors[1].Width, 0, TwoMonitors[0].Width, bar), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, swapped, after, (0, 0, TwoMonitors[1].Width, TwoMonitors[1].Height), cancel));
    }

    [Theory]
    [InlineData(Desktop.Gnome, Skip = "Waiting for its code")]
    [InlineData(Desktop.KWin, Skip = "Waiting for its code")]
    public async Task DesktopGesturesWorkOnLinux(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(KindOf(row), [TwoMonitors[0]], cancel);
        var first = Scene.PanelOverlay;
        var second = Scene.Overlay with { Position = Scene.OnWindow };
        var fading = Scene.Overlay with { Position = Fading, Fade = true };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [first, second, fading] }, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (_, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        var top = row == Desktop.Gnome ? await Scene.PanelTopAsync(desktop, height, cancel) : Scene.OnPanel.Y;
        var value = (Scene.OnPanel.X + 8, top + (height / 2));

        // A value lifted and dropped on the other overlay; the last pill lifted and dropped on the desktop.
        await HoldAndDropAsync(desktop, thermi, value, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), cancel);
        var joined = await Settings.ReadAsync(desktop.Session, cancel);
        // The first overlay is shorter by the value it gave: its last pill ends where its picture ends now.
        var (shorter, _) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(feed.Snapshot, 1, cancel), cancel);
        await HoldAndDropAsync(desktop, thermi, (Scene.OnPanel.X + shorter - 16, value.Item2), (900, 800), cancel);
        var separated = await Settings.ReadAsync(desktop.Session, cancel);
        var (changed, kept) = await CancelledAsync(desktop, thermi, value, cancel);
        var grew = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel, count: 2);
        await desktop.Input.WheelAsync(value.Item1, value.Item2, 2, "Control_L", cancel);
        await grew;
        var grown = await Settings.ReadAsync(desktop.Session, cancel);

        Assert.Equal(first.Items.Count - 1, joined.Overlays[0].Items.Count);
        Assert.Equal(second.Items.Count + 1, joined.Overlays[1].Items.Count);
        Assert.Equal(4, separated.Overlays.Count);
        Assert.NotNull(separated.Overlays[3].Snap);
        Assert.Equal(0, changed);
        Assert.Equal(ThermiConfigJson.Write(separated), ThermiConfigJson.Write(kept));
        Assert.Equal(1 + (2 * OverlaySizes.Step), grown.Overlays[0].Size, 6);
        await FadesAsync(desktop, thermi, row, cancel);
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
    // changed on screen, and the settings after.
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
            },
            cancel);
        var cancelled = await desktop.ScreenshotAsync(cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, screen, cancel);
        var changed = await Pictures.ChangedAsync(desktop.Session, screen, cancelled, (0, 0, width, height), cancel);
        return (changed, await Settings.ReadAsync(desktop.Session, cancel));
    }

    // Fade near the pointer: GNOME's extension knows where the pointer is anywhere, KWin tells thermi only over its surface.
    // What fades is the overlay itself: the rest of the screen stays as it was.
    private static async Task FadesAsync(TestDesktop desktop, SessionProcess thermi, Desktop row, CancellationToken cancel)
    {
        var shown = await desktop.ScreenshotAsync(cancel);
        var near = row == Desktop.Gnome ? (Fading.X - 40, Fading.Y - 40) : (Fading.X + 8, Fading.Y + 8);
        var faded = Traced.NextAsync(thermi.Error, Traced.Faded, cancel);
        await desktop.Input.MoveAsync(near.Item1, near.Item2, cancel);
        await faded;
        var screen = await desktop.ScreenshotAsync(cancel);
        var (width, _) = await Pictures.SizeAsync(desktop.Session, shown, cancel);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, screen, (Fading.X, Fading.Y, 200, 40), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, shown, screen, (0, 0, width, Fading.Y - 50), cancel));
    }

    private static DesktopKind KindOf(Desktop row) => row switch
    {
        Desktop.Gnome => DesktopKind.Gnome,
        Desktop.KWin => DesktopKind.KWin,
        Desktop.X11 => DesktopKind.X11,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown desktop."),
    };
}
