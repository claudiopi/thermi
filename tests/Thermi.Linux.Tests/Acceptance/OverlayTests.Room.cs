using Thermi.Core.Settings;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>The overlay within the room its monitor leaves it, as on Windows.</summary>
public sealed partial class OverlayTests
{
    // An overlay at its largest, one pill twice as wide as its 1024 px monitor, in the top left corner shows smaller, as
    // Windows' does: whole within the work area, a margin from its right edge too.
    [Fact(Timeout = Bound.Test)]
    public async Task AnOverlayLargerThanItsMonitorShowsWithinIt()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [new TestMonitor(1024, 768)], cancel, PanelEdge.Bottom);
        var style = OverlayConfig.First.Style with { Layout = PillLayout.One };
        var large = OverlayConfig.First with { Size = OverlayConfig.MaxSize, Snap = new SnapConfig(SnapSpot.TopLeft), Style = style };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [large] }, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        await desktop.ShowThermiAsync([], cancel);
        var screen = await desktop.ScreenshotAsync(cancel);

        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, before, screen, (100, 70, 200, 20), cancel));
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, screen, (1024 - 7, 0, 7, 700), cancel));
    }
}
