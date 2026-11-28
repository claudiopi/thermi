using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// The box overlay <paramref name="overlay"/> shows at, as its painter measures it: within <paramref name="room"/>, which
/// gives the width it may take at a size, or at its own size when null.
/// </summary>
public delegate PixelRect OverlayFit(int overlay, Func<(int Width, int Height), int?>? room);

/// <summary>
/// Overlays that share a horizontal taskbar, laid out together so none covers another: one that overlaps the next pushes
/// it along, toward the notification area, a margin away, and never back toward the taskbar's start, where the app
/// buttons are. One that grows past the room the others leave toward the notification area's edge, or where the last was
/// dropped past it, shows smaller. On a taskbar with parts no overlay covers, as GNOME's top bar, each room between them
/// is such a row of its own. Overlays that do not overlap stay where their snaps put them. A gesture or an edit that
/// pushes one stores where it was pushed to as its own place (<see cref="Settle"/>), so it never moves back by itself.
/// </summary>
public static class TaskbarRow
{
    /// <summary>
    /// Where overlay <paramref name="index"/> shows at <paramref name="size"/>, as <see cref="Snaps.Locate"/> places it, kept
    /// clear of every other overlay on its taskbar at the size it shows at, as <paramref name="shown"/> boxes it, null for
    /// one not shown.
    /// </summary>
    public static Position? Locate(
        IReadOnlyList<OverlayConfig> overlays,
        int index,
        IReadOnlyList<Screen> screens,
        (int Width, int Height) size,
        IReadOnlyList<PixelRect?> shown,
        TimeSpan running = default)
    {
        if (Snaps.Locate(overlays[index], screens, size.Width, size.Height, running) is not { } own)
        {
            return null;
        }

        if (Row(overlays, index, screens, size, shown) is not var (screen, bar, row))
        {
            return own;
        }

        var lefts = Apart(row, screen, bar);
        var place = row.FindIndex(each => each.At == index);
        return own with { X = own.X + lefts[place] - row[place].Left };
    }

    /// <summary>
    /// How wide overlay <paramref name="index"/> may show on its taskbar, at a size, beside the others at the sizes they show
    /// at: from where it shows to where the last may end, less the widths of those after it and a margin before each. An
    /// overlay that grows past it shows at this width, so it never pushes another off the taskbar, back over the app
    /// buttons, or over another; but never narrower than at 100 %, or its own size when smaller, so one is never squeezed
    /// to nothing where the room shrank by itself, as when the notification area grew: the one before it is then held back
    /// over the app buttons. Null off a taskbar or alone on it, whatever its size, so an overlay alone need not measure
    /// itself to know it has all the room.
    /// While the settings window is open, which widens an overlay by its alert-only items, <paramref name="closed"/> is its
    /// size with the window closed: on a taskbar with parts, as GNOME's top bar, one that sits in a room between them at
    /// that size keeps to that room, alone there too, at any width it leaves, so the window never moves it off its bar.
    /// </summary>
    public static Func<(int Width, int Height), int?>? RoomOf(
        IReadOnlyList<OverlayConfig> overlays,
        int index,
        IReadOnlyList<Screen> screens,
        IReadOnlyList<PixelRect?> shown,
        (int Width, int Height)? closed = null)
    {
        var held = Held(overlays[index], screens, closed);
        return Shared(overlays, index, screens, shown) || held is not null
            ? size => Room(overlays, index, screens, size, shown, held) is { } room
                ? Math.Max(room, held is null ? (int)Math.Ceiling(size.Width / Math.Max(1, Snaps.SizeShown(overlays[index], screens))) : 1)
                : null
            : null;
    }

    /// <summary>Whether an overlay snapped onto a horizontal taskbar shows at <paramref name="box"/> off it, where no room on it holds it.</summary>
    public static bool Beside(OverlayConfig overlay, IReadOnlyList<Screen> screens, PixelRect box) =>
        Snaps.OnTaskbar(overlay, screens) is var (_, bar) && !box.Overlaps(bar);

    /// <summary>
    /// The settings after an edit or a gesture, with each overlay it pushed along a taskbar given the place it shows at as
    /// its own (<see cref="Settle"/>), the overlays fitted as <see cref="Fitted"/> fits them from how they showed,
    /// <paramref name="shown"/>; refused when it moved one onto a taskbar without room for it (<see cref="Crowded"/>). The
    /// window's edits, the overlays' gestures and a drag's preview of its drop all ask this.
    /// </summary>
    public static EditResult Settled(ThermiConfig before, ThermiConfig after, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown, OverlayFit fit)
    {
        var (fitted, natural) = Fitted(after.Overlays, screens, shown, fit);
        if (Crowded(before.Overlays, after.Overlays, screens, natural, shown))
        {
            return new EditResult(before, Refusal.NoRoomOnTaskbar);
        }

        var overlays = Settle(after.Overlays, screens, fitted);
        return new EditResult(overlays.SequenceEqual(after.Overlays) ? after : after with { Overlays = overlays });
    }

    /// <summary>
    /// Each overlay's box as its own window fits it, each in its turn: one grown past the room the others leave on its
    /// taskbar shows smaller, the others not yet fitted counted as they showed, <paramref name="shown"/>, or at their own
    /// size. With each one's own size; null for a hidden one, whose window shows nowhere and takes no room from the others.
    /// While the settings window is open, <paramref name="closed"/> measures each as it shows with the window closed, as
    /// <see cref="RoomOf"/> keeps it to its room by that size.
    /// </summary>
    public static (List<PixelRect?> Fitted, List<PixelRect?> Natural) Fitted(
        IReadOnlyList<OverlayConfig> overlays, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown, OverlayFit fit, OverlayFit? closed = null)
    {
        var natural = overlays.Select((overlay, at) => overlay.Hidden ? null : (PixelRect?)fit(at, null)).ToList();
        var sizes = natural.Select((size, at) => size is null ? null : shown.ElementAtOrDefault(at) ?? size).ToList();
        for (var at = 0; at < sizes.Count; at++)
        {
            if (sizes[at] is not null)
            {
                var shut = closed?.Invoke(at, null) is { } box ? (box.Width, box.Height) : ((int, int)?)null;
                sizes[at] = RoomOf(overlays, at, screens, sizes, shut) is { } room ? fit(at, room) : natural[at];
            }
        }

        return (sizes, natural);
    }

    /// <summary>
    /// The overlays after a gesture or an edit, with each one on a taskbar that shows pushed along by another, at the sizes
    /// <paramref name="shown"/> boxes them, given the place it shows at as its own offset: as one undo step with what pushed
    /// it, it then stays there when the one that pushed it hides, shrinks or moves away. One held back toward the taskbar's
    /// start, where the room is short, keeps its own place, which it shows at again once there is room.
    /// </summary>
    public static IReadOnlyList<OverlayConfig> Settle(IReadOnlyList<OverlayConfig> overlays, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown)
    {
        var settled = overlays.ToList();
        for (var at = 0; at < overlays.Count; at++)
        {
            if (at < shown.Count
                && shown[at] is { } box
                && Snaps.OnTaskbar(overlays[at], screens) is var (_, bar)
                && Snaps.Locate(overlays[at], screens, box.Width, box.Height) is { } own
                && Locate(overlays, at, screens, (box.Width, box.Height), shown) is { } pushed
                && pushed.X > own.X)
            {
                settled[at] = overlays[at] with { Snap = overlays[at].Snapped! with { Offset = pushed.X - bar.Left } };
            }
        }

        return settled;
    }

    /// <summary>
    /// Whether an edit or a gesture moved an overlay onto or along a taskbar where the others, or the parts no overlay
    /// covers, as GNOME's clock, leave it no room at its own size, as <paramref name="natural"/> boxes each overlay of
    /// <paramref name="after"/>, the others counted at most as <paramref name="shown"/> boxes them: such a move is refused,
    /// so no overlay ever shows smaller or beside the taskbar for it. One new, as a pill dragged off, counts as moved; with
    /// fewer overlays than before, none does.
    /// </summary>
    public static bool Crowded(
        IReadOnlyList<OverlayConfig> before,
        IReadOnlyList<OverlayConfig> after,
        IReadOnlyList<Screen> screens,
        IReadOnlyList<PixelRect?> natural,
        IReadOnlyList<PixelRect?> shown)
    {
        if (after.Count < before.Count)
        {
            return false;
        }

        List<PixelRect?> sizes =
            [.. natural.Select((box, at) => box is { } own && shown.ElementAtOrDefault(at) is { } now && now.Width < own.Width ? now : box)];
        return Enumerable.Range(0, after.Count).Any(at =>
            natural.ElementAtOrDefault(at) is { } box
            && (at >= before.Count || before[at].Snap != after[at].Snap || before[at].Position != after[at].Position)
            && Snaps.OnTaskbar(after[at], screens) is var (screen, bar)
            && (!BarParts.Fits(screen, bar, box.Width)
                || (Shared(after, at, screens, sizes) && Room(after, at, screens, (box.Width, box.Height), sizes) < box.Width)));
    }

    // Where an overlay sits at its size with the window closed, on a taskbar with parts, in the room between them it lies in.
    private static (PixelRect Box, (int Left, int Right) Lane)? Held(OverlayConfig overlay, IReadOnlyList<Screen> screens, (int Width, int Height)? closed) =>
        closed is var (width, height)
        && Snaps.OnTaskbar(overlay, screens) is var (screen, bar)
        && BarParts.Any(screen)
        && Snaps.Locate(overlay, screens, width, height) is { } at
        && BarParts.Lane(screen, bar, Box(at, (width, height))) is { } lane
            ? (Box(at, (width, height)), lane)
            : null;

    // None for one that shows beside the taskbar, where its parts leave it no room, but one held in its room.
    private static int? Room(
        IReadOnlyList<OverlayConfig> overlays,
        int index,
        IReadOnlyList<Screen> screens,
        (int Width, int Height) size,
        IReadOnlyList<PixelRect?> shown,
        (PixelRect Box, (int Left, int Right) Lane)? held = null)
    {
        if (Row(overlays, index, screens, size, shown, held) is not var (screen, bar, row))
        {
            return null;
        }

        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var place = row.FindIndex(each => each.At == index);
        var after = row.Skip(place + 1).Sum(each => each.Width + gap);

        // One beside the notification area grows toward the taskbar's start, up to the one before it.
        var lefts = Along(row, gap);
        var from = overlays[index].Snapped?.Offset is not null ? lefts[place]
            : place > 0 ? lefts[place - 1] + row[place - 1].Width + gap
            : bar.Left;
        var packed = End(row, screen, bar) - from - after;

        // Touching is no overlap: up to where the next one sits without this one, it pushes none, so needs no margin.
        return place + 1 < row.Count ? Math.Max(packed, Along([.. row.Where(each => each.At != index)], gap)[place] - from) : packed;
    }

    private static bool Shared(IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown) =>
        Snaps.OnTaskbar(overlays[index], screens) is var (screen, _)
        && Enumerable.Range(0, Math.Min(overlays.Count, shown.Count)).Any(at => at != index && shown[at] is not null && On(overlays[at], screens, screen));

    private static bool On(OverlayConfig overlay, IReadOnlyList<Screen> screens, Screen screen) =>
        Snaps.OnTaskbar(overlay, screens) is var (other, _) && other.Id == screen.Id;

    private static (Screen Screen, PixelRect Bar, List<(int At, int Left, int Width)> Row)? Row(
        IReadOnlyList<OverlayConfig> overlays,
        int index,
        IReadOnlyList<Screen> screens,
        (int Width, int Height) size,
        IReadOnlyList<PixelRect?> shown,
        (PixelRect Box, (int Left, int Right) Lane)? held = null)
    {
        if (Snaps.OnTaskbar(overlays[index], screens) is not var (screen, bar))
        {
            return null;
        }

        var placed = Placed(overlays, index, screens, size, shown, (screen, bar));
        var own = placed.FindIndex(each => each.At == index);
        if (held is var (from, lane) && placed[own].Lane != lane)
        {
            // Held in its room, it grows from where it sits with the window closed.
            placed[own] = (index, Box(new Position(from.Left, from.Top), size), lane);
        }

        if (placed[own].Lane is not var (left, right))
        {
            return null;
        }

        // Those in its room between the taskbar's parts, in the order they lie in, one beside the notification area by its
        // edge there, so its growing never puts it before another.
        var end = Snaps.RightEnd(screen, bar);
        var row = placed
            .Where(each => each.Lane == (left, right))
            .Select(each => (each.At, each.Box.Left, each.Box.Width))
            .OrderBy(each => overlays[each.At].Snapped?.Offset is null ? end : each.Left)
            .ThenBy(each => each.At)
            .ToList();
        return (screen, bar with { Left = left, Right = right }, row);
    }

    // Each overlay on the taskbar, overlay index at size and the others as they show, where it shows and in which room.
    private static List<(int At, PixelRect Box, (int Left, int Right)? Lane)> Placed(
        IReadOnlyList<OverlayConfig> overlays,
        int index,
        IReadOnlyList<Screen> screens,
        (int Width, int Height) size,
        IReadOnlyList<PixelRect?> shown,
        (Screen Screen, PixelRect Bar) on)
    {
        var sizes = Enumerable.Range(0, overlays.Count)
            .Select(at => at == index ? size : at < shown.Count && shown[at] is { } box ? (box.Width, box.Height) : ((int Width, int Height)?)null)
            .ToList();
        return [.. Enumerable.Range(0, overlays.Count)
            .Where(at => sizes[at] is not null && On(overlays[at], screens, on.Screen))
            .Select(at => (At: at, Box: Box(Snaps.Locate(overlays[at], screens, sizes[at]!.Value.Width, sizes[at]!.Value.Height)!, sizes[at]!.Value)))
            .Select(each => (each.At, each.Box, BarParts.Lane(on.Screen, on.Bar, each.Box)))];
    }

    private static PixelRect Box(Position at, (int Width, int Height) size) => new(at.X, at.Y, at.X + size.Width, at.Y + size.Height);

    // The last keeps to the notification area's edge, or to where it was dropped past it, and never leaves its room.
    private static int End(List<(int At, int Left, int Width)> row, Screen screen, PixelRect room) =>
        Math.Min(room.Right, Math.Max(row[^1].Left + row[^1].Width, Snaps.RightEnd(screen, Snaps.TaskbarOf(screen)!.Value)));

    // Each that the one before it overlaps pushed along, a margin away, toward the notification area only; touching is no overlap.
    private static int[] Along(List<(int At, int Left, int Width)> row, int gap)
    {
        var lefts = row.Select(each => each.Left).ToArray();
        for (var at = 1; at < row.Count; at++)
        {
            var end = lefts[at - 1] + row[at - 1].Width;
            lefts[at] = lefts[at] < end ? end + gap : lefts[at];
        }

        return lefts;
    }

    // Overlays fitted to their room never pass the end; one that still would, as while another's new size has not shown
    // yet, is held at the end, and those before it that then overlap it back, a margin away, so none covers another or
    // leaves the taskbar.
    private static int[] Apart(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar)
    {
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var lefts = Along(row, gap);
        var end = End(row, screen, bar);
        lefts[^1] = Math.Min(lefts[^1], end - row[^1].Width);
        for (var at = row.Count - 2; at >= 0; at--)
        {
            lefts[at] = lefts[at] + row[at].Width > lefts[at + 1] ? lefts[at + 1] - gap - row[at].Width : lefts[at];
        }

        return [.. lefts.Select(left => Math.Max(bar.Left, left))];
    }
}
