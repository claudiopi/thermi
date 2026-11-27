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
    private static readonly TestMonitor[] TwoMonitors = [new(1920, 1080), new(1920, 1080)];

    public enum Desktop
    {
        Gnome,
        KWin,
        X11,
    }

    [Theory]
    [InlineData(Desktop.Gnome, Skip = "Waiting for its code")]
    [InlineData(Desktop.KWin, Skip = "Waiting for its code")]
    [InlineData(Desktop.X11, Skip = "Waiting for its code")]
    public async Task TheOverlayIsDraggedAndStaysOnGnome(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(KindOf(row), TwoMonitors, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [OverlayConfig.First with { Position = Scene.OnWindow }] }, cancel);
        await SensorFeed.ServeAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(SensorFeed.Desktop, 1, cancel), cancel);
        var right = TwoMonitors[0].Width + TwoMonitors[1].Width;

        // Into the second monitor's bottom right corner, then by its middle onto its panel or top bar.
        await desktop.Input.DragAsync((Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), (right - 4, TwoMonitors[1].Height - 4), cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var cornered = (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0];
        await desktop.Input.DragAsync((right - (width / 2) - 8, TwoMonitors[1].Height - (height / 2) - 8), (right - 900, 4), cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var onPanel = (await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0];
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        await desktop.SwapMonitorsAsync(TwoMonitors, cancel);
        await desktop.ShowThermiAsync([], cancel);
        var after = await desktop.ScreenshotAsync(cancel);

        Assert.Equal(SnapSpot.BottomRight, cornered.Snap?.Spot);
        Assert.Null(cornered.Position);
        Assert.Equal(SnapSpot.Taskbar, onPanel.Snap?.Spot);
        Assert.Null(onPanel.Position);
        Assert.Equal(cornered.Snap!.Monitor, onPanel.Snap!.Monitor);
        // The second monitor is now on the left: its top edge shows the overlay, and the first's shows none.
        var panel = (int)(height * 2.5);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, before, after, (0, 0, TwoMonitors[1].Width, panel), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, after, (TwoMonitors[1].Width, 0, TwoMonitors[0].Width, panel), cancel));
    }

    [Theory]
    [InlineData(Desktop.Gnome, Skip = "Waiting for its code")]
    [InlineData(Desktop.KWin, Skip = "Waiting for its code")]
    public async Task DesktopGesturesWorkOnLinux(Desktop row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(KindOf(row), [TwoMonitors[0]], cancel);
        var first = OverlayConfig.First with { Position = Scene.OnPanel };
        var second = OverlayConfig.First with { Position = Scene.OnWindow, Fade = true };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [first, second] }, cancel);
        await SensorFeed.ServeAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, await desktop.PreviewAsync(SensorFeed.Desktop, 1, cancel), cancel);
        var value = (Scene.OnPanel.X + 8, Scene.OnPanel.Y + (height / 2));
        Task Lifted(CancellationToken held)
        {
            return TestDesktop.PaintedAsync(thermi, held);
        }

        // A value lifted and dropped on the other overlay; the last pill lifted and dropped on the desktop.
        await desktop.Input.HoldAndDropAsync(value, (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8), Lifted, cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var joined = await Settings.ReadAsync(desktop.Session, cancel);
        await desktop.Input.HoldAndDropAsync((Scene.OnPanel.X + width - 16, value.Item2), (1500, 800), Lifted, cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var separated = await Settings.ReadAsync(desktop.Session, cancel);
        var (changed, kept) = await CancelledAsync(desktop, thermi, value, cancel);
        await desktop.Input.WheelAsync(value.Item1, value.Item2, 2, "Control_L", cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var grown = await Settings.ReadAsync(desktop.Session, cancel);

        Assert.Equal(first.Items.Count - 1, joined.Overlays[0].Items.Count);
        Assert.Equal(second.Items.Count + 1, joined.Overlays[1].Items.Count);
        Assert.Equal(3, separated.Overlays.Count);
        Assert.NotNull(separated.Overlays[2].Snap);
        Assert.Equal(0, changed);
        Assert.Equal(ThermiConfigJson.Write(separated), ThermiConfigJson.Write(kept));
        Assert.Equal(1 + (2 * OverlaySizes.Step), grown.Overlays[0].Size, 6);
        await FadesAsync(desktop, thermi, row, cancel);
    }

    // A drag that Esc cancels while the value is lifted puts everything back: what it changed on screen, and the settings after.
    private static async Task<(long Changed, ThermiConfig Kept)> CancelledAsync(
        TestDesktop desktop, System.Diagnostics.Process thermi, (int X, int Y) value, CancellationToken cancel)
    {
        var screen = await desktop.ScreenshotAsync(cancel);
        await desktop.Input.HoldAndDropAsync(
            value,
            (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8),
            async held =>
            {
                await TestDesktop.PaintedAsync(thermi, held);
                await desktop.Input.PressAsync("Escape", held);
                await TestDesktop.PaintedAsync(thermi, held);
            },
            cancel);
        var cancelled = await desktop.ScreenshotAsync(cancel);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, screen, cancel);
        var changed = await Pictures.ChangedAsync(desktop.Session, screen, cancelled, (0, 0, width, height), cancel);
        return (changed, await Settings.ReadAsync(desktop.Session, cancel));
    }

    // Fade near the pointer: GNOME's extension knows where the pointer is anywhere, KWin tells thermi only over its surface.
    private static async Task FadesAsync(TestDesktop desktop, System.Diagnostics.Process thermi, Desktop row, CancellationToken cancel)
    {
        var shown = await desktop.ScreenshotAsync(cancel);
        var near = row == Desktop.Gnome ? (Scene.OnWindow.X - 40, Scene.OnWindow.Y - 40) : (Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8);
        await desktop.Input.MoveAsync(near.Item1, near.Item2, cancel);
        await TestDesktop.PaintedAsync(thermi, cancel);
        var faded = await desktop.ScreenshotAsync(cancel);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, faded, (Scene.OnWindow.X, Scene.OnWindow.Y, 200, 40), cancel));
    }

    private static DesktopKind KindOf(Desktop row) => row switch
    {
        Desktop.Gnome => DesktopKind.Gnome,
        Desktop.KWin => DesktopKind.KWin,
        Desktop.X11 => DesktopKind.X11,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown desktop."),
    };
}
