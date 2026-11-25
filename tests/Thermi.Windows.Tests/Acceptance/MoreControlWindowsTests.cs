using System.Drawing;
using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Painting;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// Temperature units, hidden units, an item's own color, click-through and lock.
/// </summary>
public sealed class MoreControlWindowsTests
{
    // The pixels of the pills are those a solid background paints; the window is a bare one, shown as the overlay is.
    [Fact]
    public void APillCatchesClicksAtAnyOpacity()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), new Dictionary<string, Reading>(StringComparer.Ordinal), CultureInfo.GetCultureInfo("en-US"));
        using var painter = new Painter();
        using var solid = painter.Paint(items, OverlayLook.Of(new StyleConfig(), lightTaskbar: false), 1);
        using var clear = painter.Paint(items, OverlayLook.Of(new StyleConfig { Opacity = new OpacityConfig(Background: 0) }, lightTaskbar: false), 1);
        var pill = Pixels(solid).Where(pixel => solid.GetPixel(pixel.X, pixel.Y).A > 0).ToList();
        var between = BetweenTwoValues(clear);
        var at = new Position(100, 100);

        HWND caught = default, passed = default, window = default;
        TestWindows.OnOwnDesktop(() =>
        {
            using var shown = new LayeredTestWindow(clear, at);
            (caught, passed, window) = (TestWindows.WindowAt(new Position(at.X + between.X, at.Y + between.Y)), TestWindows.WindowAt(at), shown.Hwnd);
        });

        Assert.Equal((solid.Width, solid.Height), (clear.Width, clear.Height));
        Assert.DoesNotContain(pill, pixel => clear.GetPixel(pixel.X, pixel.Y).A < 1);
        Assert.Equal(1, clear.GetPixel(between.X, between.Y).A);
        Assert.Equal(window, caught);
        Assert.Equal(0, clear.GetPixel(0, 0).A);
        Assert.NotEqual(window, passed);
    }

    private static IEnumerable<Point> Pixels(Bitmap bitmap) =>
        Enumerable.Range(0, bitmap.Height).SelectMany(y => Enumerable.Range(0, bitmap.Width).Select(x => new Point(x, y)));

    // The middle of the widest stretch of bare pill on the middle row with something drawn on both sides of it.
    private static Point BetweenTwoValues(Bitmap bitmap)
    {
        var y = bitmap.Height / 2;
        var drawn = Enumerable.Range(0, bitmap.Width).Where(x => bitmap.GetPixel(x, y).A > 1).ToList();
        var (first, second) = drawn.Zip(drawn.Skip(1))
            .Where(pair => Enumerable.Range(pair.First + 1, pair.Second - pair.First - 1).All(x => bitmap.GetPixel(x, y).A == 1))
            .MaxBy(pair => pair.Second - pair.First);
        return new Point((first + second) / 2, y);
    }
}
