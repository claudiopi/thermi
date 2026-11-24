using System.Globalization;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Where a dragged overlay lands (the owner, 2026-11-24): anywhere along a taskbar, centered across it, with its marks a
/// help within reach and never a constraint, or at a corner's mark, or free where it was let go.
/// </summary>
public sealed class TaskbarDropTests
{
    private const int Width = 550;
    private const int Height = 38;

    // The QA PC: 2560 × 1440 at 100 %, a 48 px taskbar at the bottom whose notification area starts at 2210, so the mark
    // beside it lies at 2210 - 8 - 550 = 1652 and the one at the taskbar's start at 8, both at 1397, centered across it.
    private static readonly Screen Pc = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
    {
        Taskbar = new PixelRect(0, 1392, 2560, 1440),
        Tray = new PixelRect(2210, 1392, 2560, 1440),
    };

    // A second monitor at its right, its taskbar at the top, its notification area starting at 4200.
    private static readonly Screen Side = new("side", false, new PixelRect(2560, 0, 4480, 1080), new PixelRect(2560, 40, 4480, 1080), 1)
    {
        Taskbar = new PixelRect(2560, 0, 4480, 40),
        Tray = new PixelRect(4200, 0, 4480, 40),
    };

    // An overlay 550 × 38 dragged and dropped with its top-left corner at `drop`, with Alt held, or vertical, when it says
    // so. Where it lands: its snap and where it shows, or "free" where it is kept.
    [Theory]
    // On the taskbar, anywhere along it, over the app buttons or the notification area too, kept whole on it.
    [InlineData("1200,1397", "Taskbar 1200,1397")]
    [InlineData("300,1420", "Taskbar 300,1397")]
    [InlineData("2200,1397", "Taskbar 2010,1397")]
    // Its marks pull within 96 px along it, and no farther.
    [InlineData("1556,1397", "Taskbar 1652,1397")]
    [InlineData("1555,1397", "Taskbar 1555,1397")]
    [InlineData("60,1397", "Taskbar 8,1397")]
    // Its edge facing the taskbar within 96 px of it brings it onto the taskbar, and no farther.
    [InlineData("1200,1258", "Taskbar 1200,1397")]
    [InlineData("1200,1257", "free 1200,1257")]
    // The corners: within reach of a mark, its edge 96 and 97 px from it, and between a top corner's mark and the top.
    [InlineData("18,1336", "BottomLeft 8,1346")]
    [InlineData("1906,64", "TopRight 2002,64")]
    [InlineData("1905,64", "free 1905,64")]
    [InlineData("1992,18", "TopRight 2002,64")]
    // Alt held: no mark pulls it, and off the taskbar no reach either; on it, it stays on it where it was let go.
    [InlineData("alt 1600,1397", "Taskbar 1600,1397")]
    [InlineData("alt 18,1336", "free 18,1336")]
    [InlineData("alt 1200,1300", "free 1200,1300")]
    // A taskbar at the top of another monitor, from its edge 96 px below it too; a vertical overlay never goes on one.
    [InlineData("3000,0", "Taskbar 3000,1")]
    [InlineData("3000,136", "Taskbar 3000,1")]
    [InlineData("vertical 1200,1397", "free 1200,1354")]
    public void ADropLandsWhereItIsLetGoOrOnAMarkInReach(string drop, string lands)
    {
        var words = drop.Split(' ');
        var at = words[^1].Split(',').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();
        var dropped = new Position(at[0], at[1]);
        var orientation = words[0] == "vertical" ? Orientation.Vertical : Orientation.Horizontal;

        var snap = Snaps.Near([Pc, Side], orientation, Width, Height, dropped, magnets: words[0] != "alt");

        var landed = snap is null
            ? Snaps.Inside(dropped, [Pc, Side], Width, Height)
            : Snaps.Locate(new OverlayConfig { Snap = snap }, [Pc, Side], Width, Height)!;
        Assert.Equal(lands, string.Create(CultureInfo.InvariantCulture, $"{snap?.Spot.ToString() ?? "free"} {landed.X},{landed.Y}"));
    }
}
