using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.LayerShell;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// What KDE Plasma's panel holds, as plasmashell's scripting tells it, keeps an overlay on it beside its tray, as Windows'
/// sits beside the notification area, off the launcher, the task manager's buttons, the tray and the clock.
/// </summary>
public sealed class PlasmaPanelsTests
{
    // Plasma 6.6's default panel along the bottom of a 1638 by 800 monitor, as thermi's script printed it there: the
    // launcher, an empty pager, the task manager, a separator, the tray, the clock and Show desktop.
    private const string Default = """
        [{"location":"bottom","x":0,"y":0,"width":1638,"height":800,"widgets":[{"type":"org.kde.plasma.kickoff","x":8,"width":46},
        {"type":"org.kde.plasma.pager","x":8,"width":0},{"type":"org.kde.plasma.icontasks","x":58,"width":1180},
        {"type":"org.kde.plasma.marginsseparator","x":1242,"width":1},{"type":"org.kde.plasma.systemtray","x":1247,"width":236},
        {"type":"org.kde.plasma.digitalclock","x":1487,"width":106.59375},{"type":"org.kde.plasma.showdesktop","x":1598,"width":32}]}]
        """;

    // The default overlay as it showed on that panel, 660 by 38, whose place beside the tray ends a margin before it.
    private const int Width = 660;

    [Fact]
    public void AnOverlayOnThePanelSitsBesideItsTrayFromItsStartToo()
    {
        var panel = Assert.Single(PlasmaPanels.Parse(Default));
        var monitor = new ShellMonitor(0, 0, 1638, 800, 1, 0, Primary: true, FullScreen: false) { Room = new PanelRoom(0, 0, 0, 54) };
        var screens = new ShellScreens([PlasmaPanels.Boxed(monitor, [panel])], new Dictionary<(int X, int Y), string>()).Screens;

        var marks = Snaps.Marks(screens, Orientation.Horizontal, Width, 38).Where(mark => mark.Snap.Spot == SnapSpot.Taskbar).ToList();
        var dropped = Snaps.Near(screens, Orientation.Horizontal, Width, 38, new Position(60, 754))!;

        Assert.Equal(new PlasmaPanel((0, 0, 1638, 800), Bottom: true, Start: (8, 54), Centre: (58, 1238), End: (1247, 1630)), panel);
        Assert.All(marks, mark => Assert.Equal(1247 - Snaps.Margin, mark.At.X + Width));
        Assert.Equal(1247 - Snaps.Margin, Snaps.Locate(new OverlayConfig { Snap = dropped }, screens, Width, 38)!.X + Width);
    }
}
