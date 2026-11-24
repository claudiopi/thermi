using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>A rectangle in physical pixels of the whole desktop, as Windows gives it: its right and bottom edges lie just outside.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Overlaps(PixelRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public bool Contains(PixelRect other) => other.Left >= Left && other.Right <= Right && other.Top >= Top && other.Bottom <= Bottom;
}

/// <summary>
/// A monitor as Windows reports it, <see cref="Scale"/> being its DPI over 96. <see cref="Taskbar"/> is null on a
/// monitor without one, and <see cref="Tray"/> is the notification area on the main taskbar or the clock on another,
/// null when it cannot be found from its window.
/// </summary>
public sealed record Screen(string Id, bool Primary, PixelRect Bounds, PixelRect Work, double Scale)
{
    public PixelRect? Taskbar { get; init; }

    public PixelRect? Tray { get; init; }

    /// <summary>
    /// How far the taskbar's buttons reach, Start and the apps' wherever they begin, as at its left end on Windows 10 or a
    /// taskbar aligned left; null where they were not read.
    /// </summary>
    public PixelRect? Apps { get; init; }
}

/// <summary>Why a monitor's taskbar spots float at its work area's corners: its taskbar is on a side, or it hides itself.</summary>
public enum TaskbarFloat
{
    Side,
    Hides,
}

/// <summary>A press on an overlay: where the cursor went down, and where the overlay's top-left corner was then.</summary>
public sealed record Press(Position Cursor, Position Overlay);

/// <summary>How far, in pixels on each axis, the cursor may move before a press is a drag: the system's drag distance.</summary>
public readonly record struct DragDistance(int Width, int Height);

/// <summary>
/// Where an overlay sits, worked out again from the screens each time and never written to the file, so a snapped
/// edge stays put as the overlay, the taskbar or the monitors change.
/// </summary>
public static class Snaps
{
    /// <summary>Pixels at 100 % between a snapped overlay and the end, edge or notification area it lines up with.</summary>
    public const int Margin = 8;

    /// <summary>Pixels at 100 % kept free at the right of a taskbar whose notification area or clock cannot be found.</summary>
    public const int Reserve = 220;

    /// <summary>Pixels at 100 % between the work area's top and a top corner's overlay, below a maximized window's caption buttons.</summary>
    public const int BelowTop = 64;

    /// <summary>Pixels at 100 % within which an overlay dragged on the desktop drops onto a snap mark.</summary>
    public const int Pull = 32;

    private static readonly SnapSpot[] Corners = [SnapSpot.TopLeft, SnapSpot.TopRight, SnapSpot.BottomLeft, SnapSpot.BottomRight];

    private static readonly SnapSpot[] All = [SnapSpot.TaskbarLeft, SnapSpot.TaskbarCenter, SnapSpot.TaskbarRight, .. Corners];

    private static readonly SnapConfig Default = new(SnapSpot.TaskbarRight);

    /// <summary>The spots a monitor offers: its taskbar's only where it has one and the overlay is horizontal, its work area's corners always.</summary>
    public static IReadOnlyList<SnapSpot> Spots(Screen screen, Orientation orientation) =>
        orientation == Orientation.Horizontal && TaskbarOf(screen) is not null ? All : Corners;

    /// <summary>
    /// The top-left corner of an overlay of that size: at its snap, or at its free position while that still touches a
    /// monitor, moved inside that monitor's work area, and else at the default snap. A snap to a monitor that is not connected
    /// uses the same spot on the primary one.
    /// With OLED care on, it moves as <see cref="OledCare"/> says for an overlay running for <paramref name="running"/>.
    /// On a taskbar's spot, it goes to its <paramref name="row"/>'s place, as <see cref="TaskbarRow"/> lays it out, and
    /// sits just off the taskbar when that row has no room for it.
    /// Null with no monitor at all, as for a moment while the displays change, so the overlay stays where it is.
    /// </summary>
    public static Position? Locate(
        OverlayConfig overlay,
        IReadOnlyList<Screen> screens,
        int width,
        int height,
        TimeSpan running = default,
        RowPlace? row = null)
    {
        if (screens.Count == 0)
        {
            return null;
        }

        var (place, spot) = Place(overlay, screens, width, height, row);
        var (x, y) = overlay.Oled ? OledCare.Shift(spot, running) : (0, 0);
        return new Position(place.X + x, place.Y + y);
    }

    /// <summary>The monitor a snap shows on: the one it names, or the primary one when it names none or one not connected; null with none.</summary>
    public static Screen? ScreenOf(SnapConfig snap, IReadOnlyList<Screen> screens) =>
        screens.FirstOrDefault(each => snap.Monitor is not null && each.Id == snap.Monitor)
        ?? screens.FirstOrDefault(each => each.Primary)
        ?? (screens.Count > 0 ? screens[0] : null);

    /// <summary>
    /// The monitor an overlay shows on: its snap's, or the one its free position's top-left corner lies on, and else the
    /// default snap's, as <see cref="Locate"/> places it; null with no monitor.
    /// </summary>
    public static Screen? ScreenOf(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        overlay.Snapped is null
        && overlay.Position is { } free
        && screens.FirstOrDefault(screen => screen.Bounds.Contains(new PixelRect(free.X, free.Y, free.X + 1, free.Y + 1))) is { } under
            ? under
            : ScreenOf(overlay.Snapped ?? Default, screens);

    /// <summary>
    /// Where a press leaves the overlay with the cursor at <paramref name="cursor"/>: moved with the cursor once it is past
    /// the drag distance on either axis, or null for a click, which keeps the snap, and always on a locked overlay.
    /// </summary>
    public static Position? Dragged(Press press, Position cursor, DragDistance distance, bool locked)
    {
        var (x, y) = (cursor.X - press.Cursor.X, cursor.Y - press.Cursor.Y);
        return !locked && (Math.Abs(x) > distance.Width || Math.Abs(y) > distance.Height) ? new Position(press.Overlay.X + x, press.Overlay.Y + y) : null;
    }

    // The spot is the one shown, or null at a free position. An overlay its taskbar's row has no room for leaves the
    // taskbar for the band just off it, its snapped edge kept, so it never covers the app buttons, Start or another overlay.
    private static (Position Place, SnapSpot? Spot) Place(OverlayConfig overlay, IReadOnlyList<Screen> screens, int width, int height, RowPlace? row)
    {
        if (overlay.Snapped is null
            && overlay.Position is { } free
            && screens.Any(screen => screen.Bounds.Overlaps(new PixelRect(free.X, free.Y, free.X + width, free.Y + height))))
        {
            return (Inside(free, screens, width, height), null);
        }

        var snap = overlay.Snapped ?? Default;
        var screen = ScreenOf(snap, screens)!;
        var spot = Shown(snap.Spot, screen, overlay.Style.Orientation);
        var at = At(screen, spot, width, height);
        return row is { } place && spot is SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight && TaskbarOf(screen) is { } bar
            ? (InRow(at, place, spot, width, () => OffTheTaskbar(screen, bar, height)), spot)
            : (at, spot);
    }

    // Within the box its row keeps, wider than the overlay as it shows now, the overlay keeps its spot's edge.
    private static Position InRow(Position at, RowPlace row, SnapSpot spot, int width, Func<int> off) =>
        row.Left is not { } left ? at with { Y = off() }
        : spot == SnapSpot.TaskbarLeft ? at with { X = left }
        : spot == SnapSpot.TaskbarRight ? at with { X = left + row.Width - width }
        : at with { X = left + ((row.Width - width) / 2) };

    private static int OffTheTaskbar(Screen screen, PixelRect bar, int height)
    {
        var margin = Scaled(Margin, screen);
        return bar.Top + bar.Bottom > screen.Bounds.Top + screen.Bounds.Bottom ? bar.Top - margin - height : bar.Bottom + margin;
    }

    /// <summary>
    /// The snap marks an overlay of that size dragged on the desktop shows: each spot of every monitor with where the
    /// overlay would go there; the main monitor's name none, as a spot chosen in the window does. Laid among the
    /// <paramref name="rows"/>, a taskbar spot is offered at its place in its row, and not at all where the row has no
    /// room, nor where it would overlap an earlier spot's mark, as the center does the left one on a full taskbar.
    /// </summary>
    public static IReadOnlyList<(SnapConfig Snap, Position At)> Marks(
        IReadOnlyList<Screen> screens,
        Orientation orientation,
        int width,
        int height,
        RowContext? rows = null)
    {
        var marks = new List<(SnapConfig Snap, Position At)>();
        foreach (var screen in screens)
        {
            foreach (var spot in Spots(screen, orientation))
            {
                var snap = new SnapConfig(spot, screen.Primary ? null : screen.Id);
                if (Mark(screens, screen, snap, width, height, rows) is { } at
                    && !marks.Exists(mark => Math.Abs(mark.At.X - at.X) < width && Math.Abs(mark.At.Y - at.Y) < height))
                {
                    marks.Add((snap, at));
                }
            }
        }

        return marks;
    }

    // Where a spot's mark goes, laid in its row among the others, or null where the row has no room.
    private static Position? Mark(IReadOnlyList<Screen> screens, Screen screen, SnapConfig snap, int width, int height, RowContext? rows)
    {
        var at = At(screen, snap.Spot, width, height);
        var place = rows is null || rows.Overlay >= rows.Overlays.Count ? null : TaskbarRow.Place(Moved(rows, snap), rows.Overlay, screens, rows.Widths);
        return place is not { } row ? at : row.Left is null ? null : InRow(at, row, snap.Spot, width, () => at.Y);
    }

    private static List<OverlayConfig> Moved(RowContext rows, SnapConfig snap)
    {
        var overlays = rows.Overlays.ToList();
        overlays[rows.Overlay] = overlays[rows.Overlay] with { Snap = snap, Position = null };
        return overlays;
    }

    /// <summary>
    /// The mark an overlay dragged to <paramref name="at"/> drops onto: the nearest within 32 px at its monitor's scale,
    /// else, dropped in a taskbar's row, that taskbar's nearest spot, since a free overlay never sits on a taskbar; or null.
    /// Laid among the <paramref name="rows"/>, only the marks <see cref="Marks"/> offers count.
    /// </summary>
    public static SnapConfig? Near(IReadOnlyList<Screen> screens, Orientation orientation, int width, int height, Position at, RowContext? rows = null)
    {
        var marks = Marks(screens, orientation, width, height, rows);
        return marks
            .Select(mark => (
                mark.Snap,
                Distance: Math.Max(Math.Abs(mark.At.X - at.X), Math.Abs(mark.At.Y - at.Y)),
                Reach: Scaled(Pull, ScreenOf(mark.Snap, screens)!)))
            .Where(mark => mark.Distance <= mark.Reach)
            .OrderBy(mark => mark.Distance)
            .Select(mark => mark.Snap)
            .FirstOrDefault()
            ?? marks
                .Where(mark => TaskbarOf(ScreenOf(mark.Snap, screens)!) is { } bar
                    && mark.Snap.Spot is SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight
                    && bar.Contains(new PixelRect(at.X + (width / 2), at.Y + (height / 2), at.X + (width / 2) + 1, at.Y + (height / 2) + 1)))
                .OrderBy(mark => Math.Abs(mark.At.X - at.X))
                .Select(mark => mark.Snap)
                .FirstOrDefault();
    }

    /// <summary>
    /// Why the monitor's taskbar spots float, or null where they sit on its taskbar or it has none: a taskbar on a side is
    /// too narrow for the overlay, and one that hides slides over the work area and back, which would make it jump.
    /// </summary>
    public static TaskbarFloat? Floats(Screen screen) =>
        screen.Taskbar is not { } bar || !screen.Bounds.Overlaps(bar) ? null
        : bar.Height >= bar.Width ? TaskbarFloat.Side
        : screen.Work.Overlaps(bar) ? TaskbarFloat.Hides
        : null;

    /// <summary>
    /// The spot a snap shows at on its monitor: its own, or the bottom corner on its side for a taskbar spot the monitor
    /// does not offer, as on a vertical overlay or a monitor that has lost its taskbar.
    /// </summary>
    public static SnapSpot Shown(SnapSpot spot, Screen screen, Orientation orientation) =>
        Spots(screen, orientation).Contains(spot) ? spot
        : spot == SnapSpot.TaskbarLeft ? SnapSpot.BottomLeft
        : SnapSpot.BottomRight;

    private static Position At(Screen screen, SnapSpot spot, int width, int height)
    {
        var margin = Scaled(Margin, screen);
        var work = Clear(screen);
        var bar = TaskbarOf(screen) ?? work;
        var middle = bar.Top + ((bar.Height - height) / 2);
        var top = work.Top + Scaled(BelowTop, screen);
        return spot switch
        {
            SnapSpot.TaskbarLeft => new Position(bar.Left + margin, middle),
            SnapSpot.TaskbarCenter => new Position(bar.Left + ((bar.Width - width) / 2), middle),
            SnapSpot.TaskbarRight => new Position(RightEnd(screen, bar) - width, middle),
            SnapSpot.TopLeft => new Position(work.Left + margin, top),
            SnapSpot.TopRight => new Position(work.Right - margin - width, top),
            SnapSpot.BottomLeft => new Position(work.Left + margin, work.Bottom - margin - height),
            SnapSpot.BottomRight => new Position(work.Right - margin - width, work.Bottom - margin - height),
            _ => throw new ArgumentOutOfRangeException(nameof(spot), spot, "Unknown snap spot."),
        };
    }

    /// <summary>
    /// Where a free overlay at <paramref name="free"/> shows, whole and off any taskbar: inside the work area of the monitor
    /// it lies most on, its top-left corner kept when it is larger than that. A drop is written there, where it shows.
    /// </summary>
    public static Position Inside(Position free, IReadOnlyList<Screen> screens, int width, int height)
    {
        var box = new PixelRect(free.X, free.Y, free.X + width, free.Y + height);
        var work = Clear(screens.MaxBy(screen => Shared(screen.Bounds, box))!);
        var x = Math.Max(work.Left, Math.Min(free.X, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(free.Y, work.Bottom - height));
        return x == free.X && y == free.Y ? free : new Position(x, y);
    }

    private static long Shared(PixelRect one, PixelRect other) =>
        (long)Math.Max(0, Math.Min(one.Right, other.Right) - Math.Max(one.Left, other.Left))
        * Math.Max(0, Math.Min(one.Bottom, other.Bottom) - Math.Max(one.Top, other.Top));

    // A notification area that does not lie on its taskbar was not found where Windows draws it.
    internal static int RightEnd(Screen screen, PixelRect bar) =>
        screen.Tray is { Width: > 0 } tray && bar.Contains(tray) ? tray.Left - Scaled(Margin, screen) : bar.Right - Scaled(Reserve, screen);

    // A taskbar that hides takes none of the work area, and slides over its edge when shown: the band it takes then is kept
    // free, on the edge it lies nearest, so the overlay never sits under it.
    private static PixelRect Clear(Screen screen)
    {
        var work = screen.Work;
        if (screen.Taskbar is not { } bar || !screen.Bounds.Overlaps(bar) || !work.Overlaps(bar))
        {
            return work;
        }

        var bounds = screen.Bounds;
        var thickness = Math.Min(bar.Width, bar.Height);
        if (bar.Width > bar.Height)
        {
            return bar.Top + bar.Bottom > bounds.Top + bounds.Bottom
                ? work with { Bottom = work.Bottom - thickness }
                : work with { Top = work.Top + thickness };
        }

        return bar.Left + bar.Right > bounds.Left + bounds.Right ? work with { Right = work.Right - thickness } : work with { Left = work.Left + thickness };
    }

    // A taskbar counts when it lies across its monitor and outside the work area. One on a side is too narrow for the
    // overlay, and an auto-hidden one slides over the work area and back, which would make the overlay jump with it.
    internal static PixelRect? TaskbarOf(Screen screen) =>
        screen.Taskbar is { } bar && bar.Width > bar.Height && screen.Bounds.Overlaps(bar) && !screen.Work.Overlaps(bar) ? bar : null;

    internal static int Scaled(int pixels, Screen screen) => (int)Math.Round(pixels * screen.Scale);
}
