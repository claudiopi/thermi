using System.Globalization;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Where a dragged overlay lands, on a taskbar's geometry: where its app buttons end, where its notification area starts,
/// the overlay's width and the margin between them.
/// </summary>
public sealed class TaskbarDropTests
{
    private const int Width = 550;
    private const int Height = 38;

    // The QA PC: 2560 × 1440 at 100 %, a 48 px taskbar at the bottom whose notification area starts at 2210.
    private static readonly Screen Pc = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
    {
        Taskbar = new PixelRect(0, 1392, 2560, 1440),
        Tray = new PixelRect(2210, 1392, 2560, 1440),
    };

    // An overlay 550 × 38 dragged and dropped with its top-left corner at `drop`, beside app buttons from `apps`' left to its
    // right and, before it in the settings, the overlays `others` keeps on the taskbar, each a spot and a width. Where it
    // lands: its snap and where it shows, or "free" where it is kept. On any taskbar, its marks lie inside the taskbar and
    // clear of the buttons, the notification area and the other overlays, a margin apart, and none where it has no room.
    [Theory]
    // Its row ends 8 px before the notification area, with room for it from 8 px after the apps: the place beside the
    // notification area, from above with its bottom edge 95, 96 and 97 px over the taskbar, from the left, from the right,
    // diagonally, and dropped anywhere in the row: along it, over the apps, over Start, past its bottom, or just above it.
    [InlineData("0-1636", "", "1652,1259", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1652,1258", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1652,1257", "free 1652,1257")]
    [InlineData("0-1636", "", "1621,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1683,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1672,1377", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1642,1407", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1400,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "900,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "13,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1200,1420", "TaskbarRight 1652,1397")]
    [InlineData("0-1636", "", "1200,1354", "TaskbarRight 1652,1397")]
    // Room for two places after the apps, or one on each side of centered apps; one where the row is exactly as wide as
    // the overlay, none where it is a pixel short.
    [InlineData("0-1000", "", "1010,1397", "TaskbarLeft 1008,1397")]
    [InlineData("0-1000", "", "500,1397", "TaskbarLeft 1008,1397")]
    [InlineData("0-1000", "", "1652,1380", "TaskbarRight 1652,1397")]
    [InlineData("1000-1560", "", "20,1397", "TaskbarLeft 8,1397")]
    [InlineData("1000-1560", "", "1280,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1644", "", "1600,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1645", "", "1652,1397", "free 1652,1354")]
    // Beside another overlay on the taskbar, at the place left for it, or none where the other leaves too little.
    [InlineData("0-1000", "right 200", "1450,1390", "TaskbarRight 1444,1397")]
    [InlineData("0-1000", "right 200", "1100,1397", "TaskbarRight 1444,1397")]
    [InlineData("0-1000", "left 300", "1652,1397", "TaskbarRight 1652,1397")]
    [InlineData("0-1000", "right 1000", "1300,1397", "free 1300,1354")]
    // The corners: within reach of a mark, its edge 95, 96 and 97 px from it, and between a top corner's mark and the top
    // of the screen.
    [InlineData("0-1636", "", "18,1336", "BottomLeft 8,1346")]
    [InlineData("0-1636", "", "1992,1336", "BottomRight 2002,1346")]
    [InlineData("0-1636", "", "1907,64", "TopRight 2002,64")]
    [InlineData("0-1636", "", "1906,64", "TopRight 2002,64")]
    [InlineData("0-1636", "", "1905,64", "free 1905,64")]
    [InlineData("0-1636", "", "18,18", "TopLeft 8,64")]
    [InlineData("0-1636", "", "1992,18", "TopRight 2002,64")]
    [InlineData("0-1636", "", "1200,0", "free 1200,0")]
    public void ADropLandsAtTheMarkItsTaskbarHasRoomFor(string apps, string others, string drop, string lands)
    {
        var ends = apps.Split('-').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();
        var screen = Pc with { Apps = new PixelRect(ends[0], 1392, ends[1], 1440) };
        var held = others.Length == 0 ? [] : others.Split(", ").Select(each => each.Split(' ')).ToArray();
        OverlayConfig[] overlays =
        [
            .. held.Select(each => new OverlayConfig { Snap = new SnapConfig(each[0] == "left" ? SnapSpot.TaskbarLeft : SnapSpot.TaskbarRight) }),
            new() { Position = new Position(1500, 600) },
        ];
        int?[] widths = [.. held.Select(each => (int?)int.Parse(each[1], CultureInfo.InvariantCulture)), Width];
        var rows = new RowContext(overlays, overlays.Length - 1, widths);
        var at = drop.Split(',').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(lands, Landed(screen, rows, new Position(at[0], at[1])));

        PixelRect[] taken =
        [
            Wider(screen.Apps.Value),
            Wider(screen.Tray!.Value),
            .. Enumerable.Range(0, held.Length).Select(other => Wider(Shown(screen, overlays, other, widths))),
        ];
        var marks = Snaps.Marks([screen], Orientation.Horizontal, Width, Height, rows)
            .Where(mark => mark.Snap.Spot is SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight);
        foreach (var (snap, mark) in marks)
        {
            Assert.True(screen.Taskbar!.Value.Contains(Box(mark)), $"{snap.Spot}'s mark at {mark} leaves the taskbar");
            Assert.False(taken.Any(box => box.Overlaps(Box(mark))), $"{snap.Spot}'s mark at {mark} covers a button, the tray or an overlay");
        }
    }

    // What a mark keeps clear of: a box and the margin on each side of it.
    private static PixelRect Wider(PixelRect box) => box with { Left = box.Left - 8, Right = box.Right + 8 };

    private static string Landed(Screen screen, RowContext rows, Position drop)
    {
        if (Snaps.Near([screen], Orientation.Horizontal, Width, Height, drop, rows) is not { } snap)
        {
            var kept = Snaps.Inside(drop, [screen], Width, Height);
            return $"free {kept.X},{kept.Y}";
        }

        OverlayConfig[] after = [.. rows.Overlays.Select((overlay, at) => at == rows.Overlay ? overlay with { Snap = snap, Position = null } : overlay)];
        var shown = Box(Snaps.Locate(after[rows.Overlay], [screen], Width, Height, row: TaskbarRow.Place(after, rows.Overlay, [screen], rows.Widths))!);
        return $"{snap.Spot} {shown.Left},{shown.Top}";
    }

    private static PixelRect Shown(Screen screen, OverlayConfig[] overlays, int index, int?[] widths)
    {
        var width = widths[index]!.Value;
        var at = Snaps.Locate(overlays[index], [screen], width, Height, row: TaskbarRow.Place(overlays, index, [screen], widths))!;
        return new PixelRect(at.X, at.Y, at.X + width, at.Y + Height);
    }

    private static PixelRect Box(Position at) => new(at.X, at.Y, at.X + Width, at.Y + Height);
}
