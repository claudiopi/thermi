using Thermi.Core.Settings;
using Thermi.Overlay;
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

    [Theory]
    [InlineData(96, 1, 1)]
    [InlineData(144, 1, 1.5)]
    [InlineData(144, 1.5, 2.25)]
    [InlineData(96, 0.75, 0.75)]
    public void TheSizeScalesTheOverlayOverItsMonitor(uint dpi, double size, float scale) =>
        Assert.Equal(scale, OverlayWindow.ScaleOf(dpi, new OverlayConfig(size)), 4);

}
