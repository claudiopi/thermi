using Thermi.Core.Bridge;
using Thermi.Core.Sensors;

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
        var desktop = new HostDesktop(os, "#e95420", extension);

        var status = desktop.Of(new WindowStatus(Overlay: true, Driver: true, [], [], null));

        Assert.Equal((os, "#e95420", driver, fix), (status.Os, status.Accent, status.Driver, status.Extension));
        Assert.Equal(driver ? NoValue.DriverMayBeMissing : NoValue.ReadsNothing, desktop.Why(NoValue.DriverMayBeMissing));
        Assert.Equal(NoValue.Stopped, desktop.Why(NoValue.Stopped));
    }
}
