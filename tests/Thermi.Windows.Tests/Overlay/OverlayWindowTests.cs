using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Tests.Overlay;

public sealed class OverlayWindowTests
{
    [Fact]
    public void TheOverlayShowsTheArrowCursorNotTheLastOneLeftOver()
    {
        var windowClass = OverlayWindow.WindowClass(TestWindows.DefaultProcedure, default);

        Assert.False(windowClass.hCursor.IsNull);
        Assert.Equal(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW), windowClass.hCursor);
    }

    // The window's own scale until it knows the monitors; then the scale of the monitor it is about to show on, so a snap
    // to a monitor of another scale paints its first frame there at that scale.
    [Theory]
    [InlineData(144, 1, null, 1.5)]
    [InlineData(144, 1.5, null, 2.25)]
    [InlineData(96, 0.75, null, 0.75)]
    [InlineData(96, 1, 1.5, 1.5)]
    [InlineData(144, 1.5, 1.0, 1.5)]
    public void TheSizeScalesTheOverlayOverItsMonitor(uint dpi, double size, double? monitor, float scale)
    {
        Screen[] screens = monitor is { } there ? [new Screen("there", true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), there)] : [];

        Assert.Equal(scale, OverlayWindow.ScaleOf(dpi, new OverlayConfig(size), screens), 4);
    }

}
