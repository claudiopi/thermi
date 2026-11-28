using Thermi.Linux.LayerShell;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// What KDE Plasma's panel holds, as plasmashell's scripting tells it, keeps an overlay beside its tray off the tray and
/// the clock, and one at its start off the launcher, as GNOME's top bar's boxes do.
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

    [Fact]
    public void APanelsStartIsItsLauncherAndItsEndItsTrayAndWhatFollows()
    {
        var panel = Assert.Single(PlasmaPanels.Parse(Default));

        Assert.Equal(new PlasmaPanel((0, 0, 1638, 800), Bottom: true, Start: (8, 54), End: (1247, 1630)), panel);
    }
}
