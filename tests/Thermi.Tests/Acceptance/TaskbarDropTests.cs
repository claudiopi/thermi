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

    // The owner's taskbar after a pill was dragged off (the owner, 2026-11-24): the CPU overlay 662 × 38 beside the
    // notification area, the "CPU 141 W" overlay 103 × 38 to its left, a hidden third overlay, and a fourth 100 × 38 between
    // the two shown, 34 px from the first and 72 px from the second. A drag held as its words
    // say, `self` the dragged overlay's place (-1 for a lifted pill, whose overlay stays), its box `x,y,width` 38 px high
    // and the cursor, with Alt held when it says so. What it joins, and the point of it the drop joins at, or none.
    [Theory]
    // The cursor on the other overlay, or off it with the dragged overlay over it by half of either.
    [InlineData("1 1700,1397,103 1750,1416", "0 1750,1416")]
    [InlineData("1 1700,1380,103 1750,1394", "0 1750,1397")]
    [InlineData("1 1521,1397,103 1550,1416", "0 1572,1416")]
    [InlineData("1 1520,1397,103 1550,1416", "none")]
    [InlineData("1 1469,1397,103 1500,1416", "none")]
    // The big overlay over the small one, and a lifted pill over another overlay or its own.
    [InlineData("0 900,1397,662 1200,1416", "1 1263,1416")]
    [InlineData("-1 1300,1397,220 1380,1416", "3 1400,1416")]
    [InlineData("-1 1280,1393,103 1300,1412", "1 1300,1412")]
    [InlineData("-1 1900,1393,103 1950,1412", "0 1950,1412")]
    // Alt held drops it free.
    [InlineData("alt 1 1700,1397,103 1750,1416", "none")]
    public void ADragJoinsTheOverlayItIsOver(string drag, string joins)
    {
        var words = drag.Split(' ');
        var (box, cursor) = (Numbers(words[^2]), Numbers(words[^1]));
        PixelRect?[] shown = [new PixelRect(1572, 1397, 2234, 1435), new PixelRect(1263, 1397, 1366, 1435), null, new PixelRect(1400, 1397, 1500, 1435)];

        var target = DesktopGestures.JoinTarget(
            shown,
            int.Parse(words[^3], CultureInfo.InvariantCulture),
            new PixelRect(box[0], box[1], box[0] + box[2], box[1] + Height),
            new Position(cursor[0], cursor[1]),
            free: words[0] == "alt");

        Assert.Equal(joins, target is var (onto, at) ? string.Create(CultureInfo.InvariantCulture, $"{onto} {at.X},{at.Y}") : "none");
    }

    private static int[] Numbers(string word) => [.. word.Split(',').Select(each => int.Parse(each, CultureInfo.InvariantCulture))];
}
