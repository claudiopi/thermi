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
        Assert.Equal<TaskbarFloat?>([null, null, TaskbarFloat.Side, TaskbarFloat.Hides, TaskbarFloat.Side, TaskbarFloat.Hides], [.. new[] { Main, Side, onASide, hiding, onTheRight, hidingOnTop }.Select(Snaps.Floats)]);
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

    // Room beside the app buttons, and whether the overlay fits it as it shows: three items of a 50 px widest value, with
    // a 30 px label while labels show, 8 px between them. What fits only decides the window's notice: the overlay keeps
    // its labels and items either way.
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

        Assert.Equal(fits, TaskbarFit.Fits(three, gap: 8, room));

        // The room itself: from the buttons to the notification area less 12 px on the right, from the taskbar's start
        // less 24 px to the buttons on the left, none past them, and none on the center or off a taskbar.
        var apps = new PixelRect(400, 1368, 1500, 1440);
        // Past buttons that begin at the left end, the left spot has the room up to the notification area, less 24 px.
        var leftAligned = apps with { Left = 0, Right = 900 };
        Assert.Equal([676, 376, 0, null, null, 1264], new int?[]
        {
            Snaps.Room(Main, SnapSpot.TaskbarRight, apps),
            Snaps.Room(Main, SnapSpot.TaskbarLeft, apps),
            Snaps.Room(Main, SnapSpot.TaskbarRight, apps with { Right = 2300 }),
            Snaps.Room(Main, SnapSpot.TaskbarCenter, apps),
            Snaps.Room(Side, SnapSpot.TaskbarRight, apps),
            Snaps.Room(Main with { Buttons = leftAligned }, SnapSpot.TaskbarLeft, leftAligned),
        });

        // One that does not fit never covers the buttons: it sits right above the taskbar, its snapped edge where it was,
        // below a taskbar at the top, and goes back into the taskbar once it fits.
        var onTop = Main with { Work = new PixelRect(0, 72, 2560, 1440), Taskbar = new PixelRect(0, 0, 2560, 72), Tray = new PixelRect(2200, 0, 2560, 72) };
        Assert.Equal<Position>(
            fits ? [new(1588, 1375), new(12, 1375), new(1588, 7)] : [new(1588, 1368 - 12 - 57), new(12, 1368 - 12 - 57), new(1588, 72 + 12)],
            [Snaps.Locate(SnappedTo(SnapSpot.TaskbarRight), [Main], 600, 57, crowded: !fits)!, Snaps.Locate(SnappedTo(SnapSpot.TaskbarLeft), [Main], 600, 57, crowded: !fits)!, Snaps.Locate(SnappedTo(SnapSpot.TaskbarRight), [onTop], 600, 57, crowded: !fits)!]);
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

        // On a taskbar whose buttons begin at its left end, Start first, the left spot goes after them, never over Start.
        var start = new PixelRect(0, 1368, 72, 1440);
        var buttons = Snaps.LeftButtons(Main.Taskbar!.Value, start with { Right = 900 }, start);
        Assert.Equal([new PixelRect(0, 1368, 900, 1440), null], new[] { buttons, Snaps.LeftButtons(Main.Taskbar.Value, new PixelRect(0, 1368, 1500, 1440), new PixelRect(1100, 1368, 1172, 1440)) });
        Assert.Equal(900 + 12, Placed(left, [Main with { Buttons = buttons }], 600, 57).Left);
        var narrow = Placed(center, [Main], 600, 57);
        var wide = Placed(center, [Main], 840, 57);
        Assert.Equal(1280, (narrow.Left + narrow.Right) / 2);
        Assert.Equal(1280, (wide.Left + wide.Right) / 2);

        // A taller taskbar keeps the overlay in its middle.
        var taller = Main with { Work = new PixelRect(0, 0, 2560, 1340), Taskbar = new PixelRect(0, 1340, 2560, 1440), Tray = new PixelRect(2200, 1340, 2560, 1440) };
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
        var second = Texts.English.Format(CultureInfo.InvariantCulture, UiText.OverlayName, 2);
        Assert.Equal(Texts.English.Format(CultureInfo.InvariantCulture, UiText.OverlayPlace, second, Texts.English.Of(SnapSpot.TopLeft)), OverlayNames.Of(config, 1, Texts.English, [], CultureInfo.InvariantCulture));
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
