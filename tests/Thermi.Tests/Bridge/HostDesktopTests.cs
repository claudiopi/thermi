using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Tests.Bridge;

/// <summary>The fixes and words the window offers, as Core decides them from what its host says of the desktop.</summary>
public sealed class HostDesktopTests
{
    // A CPU missing its readings: Windows offers the driver and says why; Linux, which reads them with none, offers no
    // driver and says nothing was read, and in GNOME offers the extension's fix while it is missing or off.
    [Theory] // Rows: Windows, and each state of the extension on Linux.
    [InlineData(HostOs.Windows, ExtensionState.Missing, true, null)]
    [InlineData(HostOs.Linux, ExtensionState.Missing, false, ExtensionState.Missing)]
    [InlineData(HostOs.Linux, ExtensionState.Disabled, false, ExtensionState.Disabled)]
    [InlineData(HostOs.Linux, ExtensionState.Enabled, false, null)]
    public void EachDesktopOffersItsOwnFixes(HostOs os, ExtensionState? extension, bool driver, ExtensionState? fix)
    {
        var desktop = new HostDesktop(os, "#e95420", extension, Contrast: true);

        var status = desktop.Of(new WindowStatus(Overlay: true, Driver: true, [], [], null));

        Assert.Equal((os, "#e95420", true, driver, fix), (status.Os, status.Accent, status.Contrast, status.Driver, status.Extension));
        Assert.Equal(driver ? NoValue.DriverMayBeMissing : NoValue.ReadsNothing, desktop.Why(NoValue.DriverMayBeMissing));
        Assert.Equal(NoValue.Stopped, desktop.Why(NoValue.Stopped));
    }

    // The overlay made on the taskbar takes the name of the bar it sits on, as each Linux desktop calls its own; one with no
    // bar sets it on the screen's bottom edge.
    [Theory] // Rows: GNOME among other names, a desktop with panels, a barless one, and none named.
    [InlineData("ubuntu:GNOME", "Top bar")]
    [InlineData("KDE", "Panel")]
    [InlineData("Hyprland", "Bottom edge")]
    [InlineData("", "Bottom edge")]
    public void TheTaskbarsOverlayIsNamedForTheDesktopsBar(string desktops, string name)
    {
        var desktop = new HostDesktop(HostOs.Linux, Bar: HostDesktop.BarOf(desktops));

        Assert.Equal(name, Names.OfOverlays(new ThermiConfig(), desktop.Words(Texts.English))[0]);
    }

    // The map draws a monitor's taskbar along the edge it lies on: GNOME's top bar along the top, Windows' along the bottom.
    [Theory] // Rows: a bar at each edge.
    [InlineData(0, 48, BarEdge.Top)]
    [InlineData(1032, 1080, BarEdge.Bottom)]
    public void AMonitorsTaskbarLiesAlongTheEdgeItTouches(int top, int bottom, BarEdge edge)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var screen = new Screen("one", Primary: true, bounds, bounds, 1) { Taskbar = bounds with { Top = top, Bottom = bottom } };

        Assert.Equal(edge, MonitorView.Of(screen).Edge);
    }
}
