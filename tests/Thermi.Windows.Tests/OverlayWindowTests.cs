namespace Thermi.Windows.Tests;

public sealed class OverlayWindowTests
{
    [Fact]
    public void TheOverlayShowsTheArrowCursorNotTheLastOneLeftOver()
    {
        var windowClass = OverlayWindow.WindowClass(0, 0, 0);

        Assert.NotEqual(0, windowClass.Cursor);
        Assert.Equal(Native.LoadCursorW(0, Native.IdcArrow), windowClass.Cursor);
    }
}
