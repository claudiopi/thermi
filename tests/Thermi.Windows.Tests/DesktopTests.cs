namespace Thermi.Windows.Tests;

/// <summary>The monitors as the snap math gets them from Windows.</summary>
public sealed class DesktopTests
{
    // R35: a monitor is known by its EDID, or its connection, never by Windows' GDI name for it, which a restart may change.
    [Fact]
    public void EveryMonitorComesWithItsWorkAreaAndScaleAndOneIsPrimary()
    {
        var screens = Desktop.Screens();

        Assert.Single(screens, screen => screen.Primary);
        Assert.All(screens, screen =>
        {
            Assert.NotEmpty(screen.Id);
            Assert.True(screen.Bounds.Contains(screen.Work), $"{screen.Id} works in {screen.Work}, outside {screen.Bounds}");
            Assert.InRange(screen.Scale, 1, 5);
        });
        Assert.Equal(screens.Count, screens.Select(screen => screen.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(screens.Select(screen => screen.Id), Desktop.Screens().Select(screen => screen.Id));
        Assert.DoesNotContain(screens, screen => screen.Id.StartsWith(@"\\.\", StringComparison.Ordinal));

        // A listing kept from before the taskbar moved to another edge, or began to hide, still gives the work area of now.
        var before = Desktop.Monitors()
            .Select(monitor => monitor with { Screen = monitor.Screen with { Work = new Core.Placement.PixelRect(0, 0, 1, 1) } })
            .ToList();
        Assert.Equal(screens.Select(screen => screen.Work), Desktop.OnTaskbars(before, out _).Select(screen => screen.Work));
    }
}
