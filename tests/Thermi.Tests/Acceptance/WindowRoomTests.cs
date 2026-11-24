using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The settings window's size and place, and its list's and pane's widths, are plain geometry in Core.
/// </summary>
public sealed class WindowRoomTests
{
    private static Screen At(int width, int height, int left = 0) =>
        new($"screen-{left}", left == 0, new PixelRect(left, 0, left + width, height + 48), new PixelRect(left, 0, left + width, height), 1);

    [Fact]
    public void TheWindowsSizeFollowsItsRoom()
    {
        var side = At(1000, 800, left: 2560);
        var onSide = new ThermiConfig { Overlays = [new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight, side.Id) }] };
        (ThermiConfig Config, Screen[] Screens, PixelRect[] Free, PixelRect First)[] places =
        [
            (new ThermiConfig(), [At(2560, 1392)], [], new PixelRect(530, 246, 2030, 1146)),
            (new ThermiConfig(), [At(1000, 800)], [], new PixelRect(50, 40, 950, 760)),
            (new ThermiConfig(), [At(900, 700)], [], new PixelRect(0, 35, 900, 665)),
            (new ThermiConfig(), [At(1000, 800)], [new PixelRect(900, 100, 1000, 140)], new PixelRect(50, 40, 900, 760)),
            (onSide, [At(2560, 1392), side], [], new PixelRect(2610, 40, 3510, 760)),
        ];
        (int Window, ListLayout List)[] lists = [(1500, new(344, false)), (1200, new(296, false)), (860, new(275, false)), (800, new(320, true)), (360, new(306, true))];
        (int Pane, PaneColumns Columns)[] panes = [(863, new(1, 720)), (864, new(2, 720)), (2001, new(2, 880))];

        Assert.All(places, row => Assert.Equal(row.First, WindowRoom.First(row.Config, row.Screens, row.Free)));
        Assert.Equal((760, 560), WindowRoom.Minimum(new PixelRect(0, 0, 1920, 1032)));
        Assert.Equal((700, 500), WindowRoom.Minimum(new PixelRect(0, 0, 700, 500)));
        Assert.All(lists, row => Assert.Equal(row.List, WindowRoom.List(row.Window)));
        Assert.All(panes, row => Assert.Equal(row.Columns, WindowRoom.Columns(row.Pane)));
        Assert.Equal(WindowRoom.Setup([At(2560, 1392), side]), WindowRoom.Setup([side, At(2560, 1392)]));
        Assert.NotEqual(WindowRoom.Setup([At(2560, 1392), side]), WindowRoom.Setup([At(2560, 1392)]));
    }
}
