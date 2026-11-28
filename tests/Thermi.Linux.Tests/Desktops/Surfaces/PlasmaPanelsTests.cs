using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.LayerShell;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// What KDE Plasma's panel holds, as plasmashell's scripting tells it, keeps an overlay off the launcher, the task manager,
/// the tray and the clock: the task manager fills what the others leave and tells no program where its buttons end, so
/// the default panel has no room for an overlay, which shows above it, as a vertical one does.
/// </summary>
public sealed class PlasmaPanelsTests
{
    // Kubuntu 26.04's default panel, Plasma 6.6, along the bottom of a 1280 by 800 monitor, as thermi's script printed it
    // there (2026-11-28): the launcher, an empty pager, the task manager, a separator, the tray, the clock and Show desktop.
    private const string Default = """
        [{"location":"bottom","x":0,"y":0,"width":1280,"height":800,"widgets":[{"type":"org.kde.plasma.kickoff","x":8,"width":46},
        {"type":"org.kde.plasma.pager","x":8,"width":0},{"type":"org.kde.plasma.icontasks","x":58,"width":852},
        {"type":"org.kde.plasma.marginsseparator","x":914,"width":1},{"type":"org.kde.plasma.systemtray","x":919,"width":206},
        {"type":"org.kde.plasma.digitalclock","x":1129,"width":106.59375},{"type":"org.kde.plasma.showdesktop","x":1240,"width":32}]}]
        """;

    // The default overlay as it showed on that panel, 662 by 38, over the task manager's last two buttons.
    private const int Width = 662;

    [Fact]
    public void AnOverlayOnThePanelNeverCoversTheTaskManager()
    {
        var panel = Assert.Single(PlasmaPanels.Parse(Default));
        var monitor = new ShellMonitor(0, 0, 1280, 800, 1, 0, Primary: true, FullScreen: false) { Room = new PanelRoom(0, 0, 0, 54) };
        var screens = new ShellScreens([PlasmaPanels.Boxed(monitor, [panel])], new Dictionary<(int X, int Y), string>()).Screens;

        var marks = Snaps.Marks(screens, Orientation.Horizontal, Width, 38).Where(mark => mark.Snap.Spot == SnapSpot.Taskbar);
        var shown = Snaps.Locate(new OverlayConfig(), screens, Width, 38);

        Assert.Equal(new PlasmaPanel((0, 0, 1280, 800), Bottom: true, Start: (8, 54), Centre: (58, 910), End: (919, 1272)), panel);
        Assert.Empty(marks);
        Assert.Equal(new Position(1280 - Snaps.Margin - Width, 746 - Snaps.Margin - 38), shown);
    }
}
