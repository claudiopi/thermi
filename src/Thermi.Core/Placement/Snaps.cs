using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>Why a monitor's taskbar place floats in its work area: its taskbar is on a side, or it hides itself.</summary>
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
/// Where an overlay sits, worked out again from the screens each time. A corner's snap keeps its edges as the overlay, the
/// taskbar or the monitors change, and is never written as a position. On the taskbar, an overlay sits where it was
/// dropped along it, in its middle across it, and never moves for the app buttons, the notification area or another
/// overlay: it follows only what keeps it on the taskbar.
/// </summary>
public static class Snaps
{
    /// <summary>Pixels at 100 % between a snapped overlay and the end, edge or notification area it lines up with.</summary>
    public const int Margin = 8;

    /// <summary>Pixels at 100 % kept free at the right of a taskbar whose notification area or clock cannot be found.</summary>
    public const int Reserve = 220;

    /// <summary>Pixels at 100 % between the work area's top and a top corner's overlay, below a maximized window's caption buttons.</summary>
    public const int BelowTop = 64;

    /// <summary>
    /// Pixels at 100 % within which an overlay dragged on the desktop drops onto a snap mark, from its edge nearest the
    /// mark, or onto a taskbar, from its edge facing the taskbar.
    /// </summary>
    public const int Reach = 96;

    private static readonly SnapSpot[] Corners = [SnapSpot.TopLeft, SnapSpot.TopRight, SnapSpot.BottomLeft, SnapSpot.BottomRight];

    private static readonly SnapConfig Default = new(SnapSpot.Taskbar);

    /// <summary>
    /// The top-left corner of an overlay of that size: at its snap, or at its free position while that still touches a
    /// monitor, moved inside that monitor's work area, and else at the default snap. A snap to a monitor that is not connected
    /// uses the same spot on the primary one.
    /// With OLED care on, it moves as <see cref="OledCare"/> says for an overlay running for <paramref name="running"/>.
    /// Null with no monitor at all, as for a moment while the displays change, so the overlay stays where it is.
    /// </summary>
    public static Position? Locate(OverlayConfig overlay, IReadOnlyList<Screen> screens, int width, int height, TimeSpan running = default)
    {
        if (screens.Count == 0)
        {
            return null;
        }

        var (place, spot) = Place(overlay, screens, width, height);
        var (x, y) = overlay.Oled ? OledCare.Shift(spot, running) : (0, 0);
        return new Position(place.X + x, place.Y + y);
    }

    /// <summary>
    /// The size an overlay shows at: its own, but on a taskbar at most what keeps it within the taskbar's height, as a pill
    /// <see cref="OverlayLook.HeightOf"/> pixels high at 100 % grows with it, so it is never cut.
    /// </summary>
    public static double SizeShown(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        OnTaskbar(overlay, screens) is var (screen, bar)
            ? Math.Min(overlay.Size, (bar.Height - 0.01) / (OverlayLook.HeightOf(overlay.Style) * screen.Scale))
            : overlay.Size;

    /// <summary>
    /// The largest box an overlay off a taskbar shows as, so it shows whole: its monitor's work area, kept clear of a
    /// taskbar that hides, less the margin a corner keeps on each side; null on a taskbar, where <see cref="SizeShown"/>
    /// keeps it within the taskbar's height, or with no monitor.
    /// </summary>
    public static (int Width, int Height)? Most(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        OnTaskbar(overlay, screens) is null && ScreenOf(overlay, screens) is { } screen && Clear(screen) is var work && Scaled(Margin, screen) is var margin
            ? (Math.Max(1, work.Width - (2 * margin)), Math.Max(1, work.Height - (2 * margin)))
            : null;

    internal static (Screen Screen, PixelRect Bar)? OnTaskbar(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        overlay.Snapped is { Spot: SnapSpot.Taskbar } snap
        && overlay.Style.Orientation == Orientation.Horizontal
        && ScreenOf(snap, screens) is { } screen
        && TaskbarOf(screen) is { } bar
            ? (screen, bar)
            : null;

    /// <summary>The monitor a snap shows on: the one it names, or the primary one when it names none or one not connected; null with none.</summary>
    public static Screen? ScreenOf(SnapConfig snap, IReadOnlyList<Screen> screens) =>
        screens.FirstOrDefault(each => snap.Monitor is not null && each.Id == snap.Monitor)
        ?? screens.FirstOrDefault(each => each.Primary)
        ?? (screens.Count > 0 ? screens[0] : null);

    /// <summary>
    /// The monitor an overlay's settings put it on, before it is measured: its snap's, or the one its free position's
    /// top-left corner lies on, and else the default snap's; null with no monitor. One that grew across two shows on the
    /// monitor its box lies most on, as <see cref="Inside"/> places it.
    /// </summary>
    public static Screen? ScreenOf(OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        overlay.Snapped is null
        && overlay.Position is { } free
        && screens.FirstOrDefault(screen => screen.Bounds.Contains(new PixelRect(free.X, free.Y, free.X + 1, free.Y + 1))) is { } under
            ? under
            : ScreenOf(overlay.Snapped ?? Default, screens);

    /// <summary>The monitor a box shows on: the one it lies most on; null with no monitor.</summary>
    public static Screen? ScreenOf(PixelRect box, IReadOnlyList<Screen> screens) => screens.MaxBy(screen => screen.Bounds.Shared(box));

    /// <summary>
    /// Where a press leaves the overlay with the cursor at <paramref name="cursor"/>: moved with the cursor once it is past
    /// the drag distance on either axis, or null for a click, which keeps the snap, and always on a locked overlay.
    /// </summary>
    public static Position? Dragged(Press press, Position cursor, DragDistance distance, bool locked)
    {
        var (x, y) = (cursor.X - press.Cursor.X, cursor.Y - press.Cursor.Y);
        return !locked && (Math.Abs(x) > distance.Width || Math.Abs(y) > distance.Height) ? new Position(press.Overlay.X + x, press.Overlay.Y + y) : null;
    }

    private static (Position Place, SnapSpot? Spot) Place(OverlayConfig overlay, IReadOnlyList<Screen> screens, int width, int height)
    {
        if (overlay.Snapped is null
            && overlay.Position is { } free
            && screens.Any(screen => screen.Bounds.Overlaps(new PixelRect(free.X, free.Y, free.X + width, free.Y + height))))
        {
            return (Inside(free, screens, width, height), null);
        }

        var snap = overlay.Snapped ?? Default;
        var screen = ScreenOf(snap, screens)!;
        return (At(screen, snap, overlay.Style.Orientation, width, height), snap.Spot);
    }

    /// <summary>
    /// The snap marks an overlay of that size dragged on the desktop shows, each with where the overlay would go there: on
    /// every horizontal taskbar its overlay can sit on, beside the notification area and at the taskbar's start; and every
    /// monitor's corners. The main monitor's name none, as a spot chosen in the window does. A mark that would overlap one
    /// offered before it is left out.
    /// </summary>
    public static IReadOnlyList<(SnapConfig Snap, Position At)> Marks(IReadOnlyList<Screen> screens, Orientation orientation, int width, int height)
    {
        var marks = new List<(SnapConfig Snap, Position At)>();
        foreach (var screen in screens)
        {
            var monitor = screen.Primary ? null : screen.Id;
            var offered = (orientation == Orientation.Horizontal ? TaskbarMarks(screen, width, height) : [])
                .Concat(Corners.Select(corner => new SnapConfig(corner, monitor)).Select(snap => (snap, At(screen, snap, orientation, width, height))));
            foreach (var (snap, at) in offered)
            {
                if (!marks.Exists(mark => Math.Abs(mark.At.X - at.X) < width && Math.Abs(mark.At.Y - at.Y) < height))
                {
                    marks.Add((snap, at));
                }
            }
        }

        return marks;
    }

    private static IEnumerable<(SnapConfig Snap, Position At)> TaskbarMarks(Screen screen, int width, int height)
    {
        if (TaskbarOf(screen) is not { } bar || !BarParts.Fits(screen, bar, width))
        {
            yield break;
        }

        var monitor = screen.Primary ? null : screen.Id;

        // The mark beside the notification area keeps no offset, so the overlay follows the notification area as it grows.
        foreach (var offset in new int?[] { null, Scaled(Margin, screen) })
        {
            var snap = new SnapConfig(SnapSpot.Taskbar, monitor) { Offset = offset is { } start ? Along(bar, start, width) : null };
            yield return (snap, At(screen, snap, Orientation.Horizontal, width, height));
        }
    }

    /// <summary>
    /// Where an overlay dragged to <paramref name="at"/> drops. With its middle on a horizontal taskbar, onto that taskbar,
    /// wherever along it; else onto the nearest mark within <see cref="Reach"/> at its monitor's scale, counting a top
    /// corner's as reaching up to the work area's top; else, with its edge facing a taskbar within that reach of it, onto
    /// that taskbar; or null, for a free drop. On a taskbar it goes to a mark of that taskbar within reach along it, or
    /// stays where it was dropped along it. <paramref name="magnets"/> off, as with Alt held, no mark and no reach pulls it.
    /// </summary>
    public static SnapConfig? Near(IReadOnlyList<Screen> screens, Orientation orientation, int width, int height, Position at, bool magnets = true)
    {
        var box = new PixelRect(at.X, at.Y, at.X + width, at.Y + height);
        var middle = new PixelRect(at.X + (width / 2), at.Y + (height / 2), at.X + (width / 2) + 1, at.Y + (height / 2) + 1);
        var bars = orientation == Orientation.Horizontal ? Bars(screens) : [];

        // A drop on a taskbar stays on it before any corner within reach, as the bottom ones sit just above it.
        if (bars.FirstOrDefault(each => each.Bar.Contains(middle)) is { Screen: { } on })
        {
            return Onto(on, at, width, height, magnets);
        }

        if (!magnets)
        {
            return null;
        }

        // Pushed past a monitor's edge into its corner, the overlay is measured where it would show, kept inside.
        var shown = Inside(at, screens, width, height);
        return Marks(screens, orientation, width, height)
                .Where(mark => mark.Snap.Spot != SnapSpot.Taskbar)
                .Select(mark => (Mark: mark, Screen: ScreenOf(mark.Snap, screens)!))
                .Select(each => (each.Mark.Snap, Distance: Distance(each.Mark, shown, each.Screen), Reach: Scaled(Reach, each.Screen)))
                .Where(mark => mark.Distance <= mark.Reach)
                .OrderBy(mark => mark.Distance)
                .Select(mark => mark.Snap)
                .FirstOrDefault()
            ?? (bars.FirstOrDefault(each => InBand(box, each.Bar, each.Screen)) is { Screen: { } near } ? Onto(near, at, width, height, magnets) : null);
    }

    private static List<(Screen Screen, PixelRect Bar)> Bars(IReadOnlyList<Screen> screens) =>
        [.. screens.SelectMany(screen => TaskbarOf(screen) is { } bar ? [(screen, bar)] : Array.Empty<(Screen, PixelRect)>())];

    private static SnapConfig Onto(Screen screen, Position at, int width, int height, bool magnets)
    {
        var bar = TaskbarOf(screen)!.Value;
        var reach = Scaled(Reach, screen);
        var mark = magnets
            ? TaskbarMarks(screen, width, height)
                .Where(each => Math.Abs(each.At.X - at.X) <= reach)
                .OrderBy(each => Math.Abs(each.At.X - at.X))
                .Select(each => each.Snap)
                .FirstOrDefault()
            : null;
        var along = bar.Left + Along(bar, at.X - bar.Left, width);
        var offset = (BarParts.Beside(screen, bar, along, width) ?? along) - bar.Left;
        return mark ?? new SnapConfig(SnapSpot.Taskbar, screen.Primary ? null : screen.Id) { Offset = offset };
    }

    private static int Along(PixelRect bar, int offset, int width) => Math.Clamp(offset, 0, Math.Max(0, bar.Width - width));

    // Across the taskbar, with the overlay's edge that faces it no farther from it than the reach: the bottom edge from a
    // taskbar's top, the top edge from a taskbar's bottom at the top of the screen. The cursor, inside the overlay, is
    // never nearer than that edge.
    private static bool InBand(PixelRect box, PixelRect bar, Screen screen)
    {
        var middle = box.Left + (box.Width / 2);
        if (middle < bar.Left || middle >= bar.Right)
        {
            return false;
        }

        var reach = Scaled(Reach, screen);
        var below = bar.Top + bar.Bottom > screen.Bounds.Top + screen.Bounds.Bottom;
        return below ? box.Bottom >= bar.Top - reach && box.Top < bar.Bottom : box.Top <= bar.Bottom + reach && box.Bottom > bar.Top;
    }

    // A top corner's mark sits below a maximized window's caption buttons, so a drop between it and the work area's top,
    // toward the corner itself, counts as on it.
    private static int Distance((SnapConfig Snap, Position At) mark, Position at, Screen screen)
    {
        var above = mark.Snap.Spot is SnapSpot.TopLeft or SnapSpot.TopRight && at.Y < mark.At.Y && at.Y >= mark.At.Y - Scaled(BelowTop, screen);
        return Math.Max(Math.Abs(mark.At.X - at.X), above ? 0 : Math.Abs(mark.At.Y - at.Y));
    }

    /// <summary>
    /// Why the monitor's taskbar place floats, or null where it sits on its taskbar or it has none: a taskbar on a side is
    /// too narrow for the overlay, and one that hides slides over the work area and back, which would make it jump.
    /// </summary>
    public static TaskbarFloat? Floats(Screen screen) =>
        screen.Taskbar is not { } bar || !screen.Bounds.Overlaps(bar) ? null
        : bar.Height >= bar.Width ? TaskbarFloat.Side
        : screen.Work.Overlaps(bar) ? TaskbarFloat.Hides
        : null;

    private static Position At(Screen screen, SnapConfig snap, Orientation orientation, int width, int height)
    {
        var margin = Scaled(Margin, screen);
        var work = Clear(screen);
        var (right, bottom) = (Math.Max(work.Left, work.Right - margin - width), Math.Max(work.Top, work.Bottom - margin - height));

        // Below a maximized window's caption buttons while there is room for it, else as high as keeps it whole.
        var top = Math.Min(work.Top + Scaled(BelowTop, screen), bottom);
        return snap.Spot switch
        {
            SnapSpot.Taskbar => OnTaskbar(screen, snap.Offset, orientation, width, height),
            SnapSpot.TopLeft => new Position(work.Left + margin, top),
            SnapSpot.TopRight => new Position(right, top),
            SnapSpot.BottomLeft => new Position(work.Left + margin, bottom),
            SnapSpot.BottomRight => new Position(right, bottom),
            _ => throw new ArgumentOutOfRangeException(nameof(snap), snap.Spot, "Unknown snap spot."),
        };
    }

    // Where it cannot sit on a taskbar (vertical overlay, side, hidden or no taskbar, or no room clear of its parts), the
    // offset is from the monitor's left edge, and it sits beside the bar: below one at the top, as GNOME's top bar, else at
    // the work area's bottom.
    private static Position OnTaskbar(Screen screen, int? offset, Orientation orientation, int width, int height)
    {
        if (orientation == Orientation.Horizontal
            && TaskbarOf(screen) is { } bar
            && BarParts.Beside(screen, bar, bar.Left + Along(bar, offset ?? (RightEnd(screen, bar) - width - bar.Left), width), width) is { } on)
        {
            return new Position(on, bar.Top + ((bar.Height - height) / 2));
        }

        var margin = Scaled(Margin, screen);
        var work = Clear(screen);
        var left = offset is { } kept ? screen.Bounds.Left + kept : work.Right - margin - width;
        var top = screen.Taskbar is { } above && above.Bottom <= work.Top && above.Height > 0
            ? work.Top + margin
            : Math.Max(work.Top, work.Bottom - margin - height);
        return new Position(Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width)), top);
    }

    /// <summary>
    /// Where a free overlay at <paramref name="free"/> shows, whole and off any taskbar: inside the work area of the monitor
    /// it lies most on, its top-left corner kept when it is larger than that. A drop is written there, where it shows.
    /// </summary>
    public static Position Inside(Position free, IReadOnlyList<Screen> screens, int width, int height)
    {
        var box = new PixelRect(free.X, free.Y, free.X + width, free.Y + height);
        var work = Clear(ScreenOf(box, screens)!);
        var x = Math.Max(work.Left, Math.Min(free.X, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(free.Y, work.Bottom - height));
        return x == free.X && y == free.Y ? free : new Position(x, y);
    }

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

    // See Floats for why a side or auto-hidden taskbar does not count.
    internal static PixelRect? TaskbarOf(Screen screen) =>
        screen.Taskbar is { } bar && bar.Width > bar.Height && screen.Bounds.Overlaps(bar) && !screen.Work.Overlaps(bar) ? bar : null;

    internal static int Scaled(int pixels, Screen screen) => (int)Math.Round(pixels * screen.Scale);
}
