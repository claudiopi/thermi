using Thermi.Core.Placement;
using Thermi.Linux.Gnome;
using Thermi.Linux.LayerShell;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// Where a monitor's panels keep room, from what a layer-shell compositor answers thermi's probe of them: the size its
/// panels' zones leave a surface kept off them, and where it moves popups the monitor's size. Hyprland keeps every popup
/// 4 px inside its monitor, whatever its panels keep, which is no panel: only the zones say how much room there is.
/// </summary>
public sealed class LayerProbeTests
{
    // Hyprland 0.57.2 on Arch, two 1280 by 800 monitors, as a copy of the probe printed it there (2026-11-28): it keeps 61 px
    // along the first's top, and nothing on the second; on both, the popup over the whole monitor shrinks 4 px in from each
    // edge, and the one ending at the kept-off surface's corner slides 4 px in from the left.
    [Theory] // Rows: a monitor Hyprland keeps room on, and one it keeps none on.
    [InlineData(739, -57, 61)]
    [InlineData(800, 4, 0)]
    public void HyprlandsGapsAreNoPanel(int keptOff, int slidTop, int top)
    {
        var (room, _) = LayerProbe.RoomOf((1280, 800), (1280, keptOff), (4, 4, 1272, 792), (4, slidTop));
        var monitor = new ShellMonitor(0, 0, 1280, 800, 1, 0, Primary: true, FullScreen: false) { Room = room };

        var screen = Assert.Single(new ShellScreens([monitor], new Dictionary<(int X, int Y), string>()).Screens);

        PixelRect? bar = top == 0 ? null : new PixelRect(0, 0, 1280, top);
        Assert.Equal((bar, new PixelRect(0, top, 1280, 800)), (screen.Taskbar, screen.Work));
    }
}
