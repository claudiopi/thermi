using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Snapped placement is computed, never written, and survives monitors coming and going.
/// </summary>
public sealed class SnapTests
{
    // A 2560 × 1440 monitor at 150 %, with a 72 px taskbar at the bottom whose notification area starts at 2200.
    internal static readonly Screen Main = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1368), 1.5)
    {
        Taskbar = new PixelRect(0, 1368, 2560, 1440),
        Tray = new PixelRect(2200, 1368, 2560, 1440),
    };

    /// <summary>Windows' drag distance at 150 %.</summary>
    internal static readonly DragDistance Distance = new(6, 6);

    // A 1920 × 1080 monitor at 100 % to the left of the main one, without a taskbar.
    private static readonly Screen Side = new("side", false, new PixelRect(-1920, 0, 0, 1080), new PixelRect(-1920, 0, 0, 1080), 1);

    private static OverlayConfig SnappedTo(SnapSpot spot, string? monitor = null, Orientation orientation = Orientation.Horizontal) =>
        new() { Snap = new SnapConfig(spot, monitor), Style = new StyleConfig { Orientation = orientation } };

    internal static PixelRect Placed(OverlayConfig overlay, IReadOnlyList<Screen> screens, int width, int height)
    {
        var at = Snaps.Locate(overlay, screens, width, height)!;
        return new PixelRect(at.X, at.Y, at.X + width, at.Y + height);
    }

    /// <summary>A press on the overlay where it shows, the cursor at <paramref name="cursor"/>, let go at <paramref name="released"/>.</summary>
    internal static EditResult? Released(ThermiConfig config, Position cursor, Position released)
    {
        var shown = Placed(config.Overlays[0], [Main], 600, 57);
        return OverlayEdits.Release(config, 0, new Press(cursor, new Position(shown.Left, shown.Top)), released, Distance);
    }

    [Fact]
    public void TaskbarSnapsAreOfferedOnlyWhereThereIsATaskbar()
    {
        SnapSpot[] corners = [SnapSpot.TopLeft, SnapSpot.TopRight, SnapSpot.BottomLeft, SnapSpot.BottomRight];

        Assert.Equal([SnapSpot.TaskbarLeft, SnapSpot.TaskbarCenter, SnapSpot.TaskbarRight, .. corners], Snaps.Spots(Main, Orientation.Horizontal));
        Assert.Equal(corners, Snaps.Spots(Side, Orientation.Horizontal));

        // A taskbar on a side, or one that hides over the work area, offers no spots either.
        var onASide = Side with { Work = new PixelRect(-1856, 0, 0, 1080), Taskbar = new PixelRect(-1920, 0, -1856, 1080) };
        var hiding = Main with { Work = Main.Bounds, Taskbar = new PixelRect(0, 1436, 2560, 1508) };
        Assert.Equal(corners, Snaps.Spots(onASide, Orientation.Horizontal));
        Assert.Equal(corners, Snaps.Spots(hiding, Orientation.Horizontal));

        // There a taskbar spot floats at the bottom corner on its side, clear of a hiding taskbar's band, and the window says why.
        var onTheRight = Main with { Work = new PixelRect(0, 0, 2464, 1440), Taskbar = new PixelRect(2464, 0, 2560, 1440) };
        var hidingOnTop = Main with { Work = Main.Bounds, Taskbar = new PixelRect(0, -68, 2560, 4) };
        Assert.Equal<TaskbarFloat?>(
            [null, null, TaskbarFloat.Side, TaskbarFloat.Hides, TaskbarFloat.Side, TaskbarFloat.Hides],
            [.. new[] { Main, Side, onASide, hiding, onTheRight, hidingOnTop }.Select(Snaps.Floats)]);
        Assert.Equal(new PixelRect(2464 - 12 - 600, 1440 - 12 - 57, 2464 - 12, 1440 - 12), Placed(SnappedTo(SnapSpot.TaskbarCenter), [onTheRight], 600, 57));
        Assert.Equal(1368 - 12, Placed(SnappedTo(SnapSpot.TaskbarRight), [hiding], 600, 57).Bottom);
        Assert.Equal(72, Placed(SnappedTo(SnapSpot.TopLeft), [hidingOnTop], 600, 57).Top - Placed(SnappedTo(SnapSpot.TopLeft), [Main], 600, 57).Top);
    }

    [Fact]
    public void ARightSnapEndsBeforeTheNotificationArea()
    {
        var placed = Placed(SnappedTo(SnapSpot.TaskbarRight), [Main], 600, 57);

        Assert.Equal(2200 - 12, placed.Right);
        Assert.Equal(1368 + 7, placed.Top);
        Assert.Equal(placed, Placed(new OverlayConfig(), [Main], 600, 57));
    }

    [Fact]
    public void WithoutTheNotificationAreaTheRightSnapKeepsAFixedReserve()
    {
        var notFound = Main with { Tray = null };
        var notOnTheTaskbar = Main with { Tray = new PixelRect(2200, 0, 2560, 72) };

        Assert.Equal(2560 - 330, Placed(SnappedTo(SnapSpot.TaskbarRight), [notFound], 600, 57).Right);
        Assert.Equal(2560 - 330, Placed(SnappedTo(SnapSpot.TaskbarRight), [notOnTheTaskbar], 600, 57).Right);
    }

    // What an overlay keeps on a taskbar as it shows: three items of a 50 px widest value, with a 30 px label while
    // labels show, 8 px between them. The overlay keeps its labels and items whatever its row's room.
    [Theory]
    [InlineData(300, true, true)]
    [InlineData(280, true, true)]
    [InlineData(279, true, false)]
    [InlineData(279, false, true)]
    [InlineData(190, false, true)]
    [InlineData(189, false, false)]
    public void WhetherAnOverlayFitsBesideTheAppsIsMeasuredOnItsWidestValues(double room, bool labels, bool fits)
    {
        FitItem[] three = [.. Enumerable.Repeat(new FitItem(labels ? 30 : 0, 50), 3)];

        Assert.Equal(fits, TaskbarFit.Width(three, gap: 8) <= room);

        // One its row has no room for never covers the buttons: it sits right above the taskbar, its snapped edge where it
        // was, below a taskbar at the top, and goes back into the taskbar once it fits. In a row's box wider than it shows,
        // it keeps its spot's edge.
        var onTop = Main with { Work = new PixelRect(0, 72, 2560, 1440), Taskbar = new PixelRect(0, 0, 2560, 72), Tray = new PixelRect(2200, 0, 2560, 72) };
        RowPlace? row = fits ? null : new RowPlace(null, 600);
        Assert.Equal<Position>(
            fits ? [new(1588, 1375), new(12, 1375), new(1588, 7)] : [new(1588, 1368 - 12 - 57), new(12, 1368 - 12 - 57), new(1588, 72 + 12)],
            [
                Snaps.Locate(SnappedTo(SnapSpot.TaskbarRight), [Main], 600, 57, row: row)!,
                Snaps.Locate(SnappedTo(SnapSpot.TaskbarLeft), [Main], 600, 57, row: row)!,
                Snaps.Locate(SnappedTo(SnapSpot.TaskbarRight), [onTop], 600, 57, row: row)!,
            ]);
        Assert.Equal(
            [1000, 1020, 1040],
            new[] { SnapSpot.TaskbarLeft, SnapSpot.TaskbarCenter, SnapSpot.TaskbarRight }
                .Select(spot => Snaps.Locate(SnappedTo(spot), [Main], 600, 57, row: new RowPlace(1000, 640))!.X));
    }

    // Overlays on the main monitor's taskbar, whose row runs from 12 to 2188, a margin of 12 px between them, beside app
    // buttons centered from 1000 to 1560, aligned left from Start to 1500, or not read: each one's left edge, "-" for one
    // its taskbar has no room for, "x" for one not on a taskbar. The settings' first overlay takes its place first.
    [Theory]
    [InlineData("aligned-left", "left 600", "1512")]
    [InlineData("aligned-left", "center 600", "1512")]
    [InlineData("centered", "center 600", "1572")]
    [InlineData("unread", "center 600", "980")]
    [InlineData("aligned-left", "left 300, right 300", "1512 1888")]
    [InlineData("aligned-left", "left 400, left 200", "1512 1924")]
    [InlineData("aligned-left", "right 400, right 400", "1788 -")]
    [InlineData("aligned-left", "top-left 600, left 600", "x 1512")]
    [InlineData("centered", "left 900", "12")]
    [InlineData("centered", "right 700", "288")]
    [InlineData("centered", "left 1100", "-")]
    public void OverlaysOnATaskbarKeepOffItsButtonsAndOneAnother(string buttons, string overlays, string lefts)
    {
        var screen = Main with
        {
            Apps = buttons switch
            {
                "centered" => new PixelRect(1000, 1368, 1560, 1440),
                "aligned-left" => new PixelRect(0, 1368, 1500, 1440),
                _ => null,
            },
        };
        var spots = new Dictionary<string, SnapSpot>(StringComparer.Ordinal)
        {
            ["left"] = SnapSpot.TaskbarLeft,
            ["center"] = SnapSpot.TaskbarCenter,
            ["right"] = SnapSpot.TaskbarRight,
            ["top-left"] = SnapSpot.TopLeft,
        };
        var row = overlays.Split(", ").Select(each => each.Split(' ')).ToList();
        OverlayConfig[] configs = [.. row.Select(each => SnappedTo(spots[each[0]]))];
        int?[] widths = [.. row.Select(each => (int?)int.Parse(each[1], CultureInfo.InvariantCulture))];

        var laid = Enumerable.Range(0, configs.Length).Select(at => TaskbarRow.Place(configs, at, [screen], widths) switch
        {
            null => "x",
            { Left: null } => "-",
            { Left: { } left } => left.ToString(CultureInfo.InvariantCulture),
        });

        Assert.Equal(lefts, string.Join(' ', laid));
    }

    // Dragged beside an overlay that keeps 400 px at the right of a taskbar whose buttons, aligned left, reach 1500: a
    // taskbar spot is offered only where its row has room, at its place there, never over another spot's mark, as the
    // center would be over the left, and a drop in the row takes the nearest offered; with no room, none is offered and
    // the drop stays free.
    [Fact]
    public void ADraggedOverlayIsOfferedOnlyTheTaskbarPlacesWithRoom()
    {
        var screen = Main with { Apps = new PixelRect(0, 1368, 1500, 1440) };
        OverlayConfig[] overlays = [SnappedTo(SnapSpot.TaskbarRight), new() { Position = new Position(100, 100) }];
        var room = new RowContext(overlays, 1, [400, 120]);
        var none = room with { Widths = [400, 300] };
        static IEnumerable<(SnapConfig, Position)> OnTaskbar(IEnumerable<(SnapConfig Snap, Position At)> marks)
        {
            return marks.Where(mark => mark.Snap.Spot is SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight);
        }

        Assert.Equal(
            [(new SnapConfig(SnapSpot.TaskbarLeft), new Position(1512, 1375)), (new SnapConfig(SnapSpot.TaskbarRight), new Position(1666, 1375))],
            OnTaskbar(Snaps.Marks([screen], Orientation.Horizontal, 110, 57, room)));
        Assert.Empty(OnTaskbar(Snaps.Marks([screen], Orientation.Horizontal, 110, 57, none)));

        // Dropped at the row's left end, it snaps to the left spot and shows where its mark was, on the taskbar.
        var dropped = new Position(20, 1380);
        var snap = Snaps.Near([screen], Orientation.Horizontal, 110, 57, dropped, room);
        Assert.Equal(new SnapConfig(SnapSpot.TaskbarLeft), snap);
        OverlayConfig[] after = [overlays[0], overlays[1] with { Snap = snap, Position = null }];
        Assert.Equal(
            new Position(1512, 1375),
            Snaps.Locate(after[1], [screen], 110, 57, row: TaskbarRow.Place(after, 1, [screen], room.Widths)));
        Assert.Null(Snaps.Near([screen], Orientation.Horizontal, 110, 57, dropped, none));
    }

    // The buttons as Windows 11 names them: Start, two apps, then the settings window's own button, which opening the
    // window adds and which must not crowd the overlay out; the notification area's and anything off the bar never count.
    [Fact]
    public void TheAppButtonsReachLeavesOutThermisOwnButton()
    {
        var bar = Main.Taskbar!.Value;
        TaskbarElement[] read =
        [
            new(new PixelRect(0, 1368, 72, 1440), false, "ToggleButton", "StartButton"),
            new(new PixelRect(72, 1368, 138, 1440), true, "Taskbar.TaskListButtonAutomationPeer", "Appid: Microsoft.Windows.Explorer"),
            new(
                new PixelRect(138, 1368, 204, 1440),
                true,
                "Taskbar.TaskListButtonAutomationPeer",
                @"Appid: {6D809377-6AF0-444B-8957-A3773F02200E}\OBS\obs64.exe"),
            new(
                new PixelRect(204, 1368, 270, 1440),
                true,
                "Taskbar.TaskListButtonAutomationPeer",
                @"Appid: {6D809377-6AF0-444B-8957-A3773F02200E}\thermi\THERMI.exe"),
            new(new PixelRect(2210, 1368, 2258, 1440), true, "SystemTray.NormalButton", "SystemTrayIcon"),
            new(new PixelRect(2260, 1368, 2300, 1440), true, "Button", "Volume"),
            new(new PixelRect(300, 1300, 366, 1360), true, "Button", "Flyout"),
            new(new PixelRect(400, 1368, 400, 1440), true, "Button", "Hidden"),
        ];

        Assert.Equal(new TaskbarButtons(new PixelRect(0, 1368, 204, 1440)), TaskbarButtons.Of(read, bar, Main.Tray, "thermi.exe"));
        Assert.Null(TaskbarButtons.Of(read[4..], bar, Main.Tray, "thermi.exe"));

        // Among the others, the buttons after it slide back by its width: toward Start at the left end, from both sides
        // of buttons centered.
        TaskbarElement[] among = [read[0], read[1], read[3] with { Box = read[2].Box }, read[2] with { Box = read[3].Box }];
        TaskbarElement[] centered = [.. among.Select(each => each with { Box = each.Box with { Left = each.Box.Left + 1000, Right = each.Box.Right + 1000 } })];
        Assert.Equal<PixelRect>(
            [new PixelRect(0, 1368, 204, 1440), new PixelRect(1033, 1368, 1237, 1440)],
            [TaskbarButtons.Of(among, bar, Main.Tray, "thermi.exe")!.Extent, TaskbarButtons.Of(centered, bar, Main.Tray, "thermi.exe")!.Extent]);
    }

    // The top corners sit 64 px below the work area's top, at the monitor's scale.
    [Fact]
    public void ACornerSnapSitsInsideTheWorkArea()
    {
        foreach (var screen in new[] { Main, Side })
        {
            foreach (var spot in new[] { SnapSpot.TopLeft, SnapSpot.TopRight, SnapSpot.BottomLeft, SnapSpot.BottomRight })
            {
                var placed = Placed(SnappedTo(spot, screen.Id), [Main, Side], 600, 57);

                Assert.True(screen.Work.Contains(placed), $"{spot} on {screen.Id} is at {placed}");
            }
        }

        Assert.Equal(new PixelRect(12, 96, 612, 153), Placed(SnappedTo(SnapSpot.TopLeft), [Main], 600, 57));
        Assert.Equal(new PixelRect(-608, 64, -8, 121), Placed(SnappedTo(SnapSpot.TopRight, Side.Id), [Main, Side], 600, 57));
        Assert.Equal(new PixelRect(1948, 1299, 2548, 1356), Placed(SnappedTo(SnapSpot.BottomRight), [Main], 600, 57));
    }

    [Fact]
    public void ASnappedOverlayStaysAlignedWhenItGrows()
    {
        var right = SnappedTo(SnapSpot.TaskbarRight);
        var left = SnappedTo(SnapSpot.TaskbarLeft);
        var center = SnappedTo(SnapSpot.TaskbarCenter);

        Assert.Equal(Placed(right, [Main], 600, 57).Right, Placed(right, [Main], 840, 57).Right);
        Assert.Equal(Placed(left, [Main], 600, 57).Left, Placed(left, [Main], 840, 57).Left);
        Assert.Equal(12, Placed(left, [Main], 600, 57).Left);
        var narrow = Placed(center, [Main], 600, 57);
        var wide = Placed(center, [Main], 840, 57);
        Assert.Equal(1280, (narrow.Left + narrow.Right) / 2);
        Assert.Equal(1280, (wide.Left + wide.Right) / 2);

        // A taller taskbar keeps the overlay in its middle.
        var taller = Main with
        {
            Work = new PixelRect(0, 0, 2560, 1340),
            Taskbar = new PixelRect(0, 1340, 2560, 1440),
            Tray = new PixelRect(2200, 1340, 2560, 1440),
        };
        Assert.Equal(1340 + 21, Placed(right, [taller], 600, 57).Top);
    }

    // A free overlay dropped where it hangs off the screen, over the taskbar or across two monitors; where it shows.
    [Theory]
    [InlineData(100, 200, 100, 200)]
    [InlineData(2100, 200, 1960, 200)]
    [InlineData(-300, -20, 0, 0)]
    [InlineData(100, 1350, 100, 1311)]
    [InlineData(2400, 1000, 2560, 975)]
    [InlineData(2000, 1000, 1960, 1000)]
    public void AFreeOverlayShowsWholeInsideTheWorkArea(int x, int y, int left, int top)
    {
        var right = new Screen("right", false, new PixelRect(2560, 0, 4480, 1080), new PixelRect(2560, 0, 4480, 1032), 1);
        var overlay = new OverlayConfig { Position = new Position(x, y) };

        Assert.Equal(new PixelRect(left, top, left + 600, top + 57), Placed(overlay, [Main, right], 600, 57));

        // A drop there is written where the overlay shows, so the file and the screen agree.
        Assert.Equal(new Position(left, top), Snaps.Inside(new Position(x, y), [Main, right], 600, 57));
    }

    [Fact]
    public void ASnappedVerticalOverlayKeepsItsBottomEdge()
    {
        var overlay = SnappedTo(SnapSpot.BottomLeft, orientation: Orientation.Vertical);

        var shorter = Placed(overlay, [Main], 200, 228);
        var taller = Placed(overlay, [Main], 200, 290);

        Assert.Equal(1368 - 12, shorter.Bottom);
        Assert.Equal(shorter.Bottom, taller.Bottom);
        Assert.Equal(shorter.Left, taller.Left);
    }

    [Fact]
    public void ASnappedOverlayNeverWritesItsPosition()
    {
        var config = new ThermiConfig { Overlays = [SnappedTo(SnapSpot.TaskbarCenter)] };
        var pressed = new Position(1280, 1400);

        // Wherever it is computed to be as it grows, a press let go where it started has nothing to write.
        foreach (var width in new[] { 600, 700, 800 })
        {
            var shown = Placed(config.Overlays[0], [Main], width, 57);
            Assert.Null(OverlayEdits.Release(config, 0, new Press(pressed, new Position(shown.Left, shown.Top)), pressed, Distance));
        }

        Assert.Null(config.Overlays[0].Position);
        Assert.DoesNotContain("\"position\"", File(config), StringComparison.Ordinal);
    }

    [Fact]
    public void DraggingASnappedOverlayUnsnapsIt()
    {
        var config = new ThermiConfig { Overlays = [SnappedTo(SnapSpot.TaskbarRight)] };
        var pressed = new Position(1900, 1400);
        var shown = Placed(config.Overlays[0], [Main], 600, 57);

        var moved = Done(Released(config, pressed, new Position(1900 + 5, 1400 - 300))!);

        Assert.Null(moved.Overlays[0].Snap);
        Assert.Equal(new Position(shown.Left + 5, shown.Top - 300), moved.Overlays[0].Position);
        Assert.Equal(new PixelRect(shown.Left + 5, shown.Top - 300, shown.Right + 5, shown.Bottom - 300), Placed(moved.Overlays[0], [Main], 600, 57));
        Assert.DoesNotContain("\"snap\"", File(moved), StringComparison.Ordinal);
        Assert.Contains("\"position\"", File(moved), StringComparison.Ordinal);

        // A free position left on a monitor that is gone goes back to the default snap.
        var gone = Done(Released(config, pressed, new Position(-1500, 500))!);
        Assert.Equal(Placed(new OverlayConfig(), [Main], 600, 57), Placed(gone.Overlays[0], [Main], 600, 57));
        Assert.Equal(Refusal.NotFound, OverlayEdits.Release(config, 1, new Press(pressed, pressed), new Position(0, 0), Distance)!.Refused);
    }

    [Fact]
    public void AClickKeepsTheSnap()
    {
        var config = new ThermiConfig { Overlays = [SnappedTo(SnapSpot.TaskbarRight)] };
        var pressed = new Position(1900, 1400);

        Assert.Null(Released(config, pressed, pressed));
        Assert.Null(Released(config, pressed, new Position(1906, 1394)));
        Assert.NotNull(Released(config, pressed, new Position(1900, 1407)));
        Assert.Null(Snaps.Dragged(new Press(pressed, new Position(100, 200)), new Position(1906, 1394), Distance, locked: false));
        Assert.Equal(new Position(107, 200), Snaps.Dragged(new Press(pressed, new Position(100, 200)), new Position(1907, 1400), Distance, locked: false));
    }

    [Fact]
    public void WithNoMonitorAtAllTheOverlayStaysWhereItIs()
    {
        var config = new ThermiConfig { Overlays = [SnappedTo(SnapSpot.TaskbarRight), new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopLeft, "side") }] };

        Assert.Null(Snaps.Locate(config.Overlays[0], [], 600, 57));
        Assert.Null(Snaps.ScreenOf(new SnapConfig(SnapSpot.TopLeft), []));
        Assert.Equal(Texts.English.Of(NameText.TopLeft), OverlayNames.Of(config, 1, Texts.English));
    }

    [Fact]
    public void ASnapToAMonitorThatIsGoneFallsBackToThePrimaryAndComesBack()
    {
        var overlay = SnappedTo(SnapSpot.TopRight, "side");

        var there = Placed(overlay, [Main, Side], 600, 57);
        var gone = Placed(overlay, [Main], 600, 57);
        var back = Placed(overlay, [Side, Main], 600, 57);

        Assert.Equal(new PixelRect(-608, 64, -8, 121), there);
        Assert.Equal(Placed(SnappedTo(SnapSpot.TopRight), [Main], 600, 57), gone);
        Assert.Equal(there, back);

        // A taskbar snap on a monitor that has none shows at the bottom corner on its side.
        Assert.Equal(new PixelRect(-608, 1015, -8, 1072), Placed(SnappedTo(SnapSpot.TaskbarCenter, "side"), [Main, Side], 600, 57));
        Assert.Equal(new PixelRect(-1912, 1015, -1312, 1072), Placed(SnappedTo(SnapSpot.TaskbarLeft, "side"), [Main, Side], 600, 57));
    }
}
