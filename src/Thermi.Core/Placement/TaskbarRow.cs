using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// Overlays that share a horizontal taskbar, laid out together so none covers another: one that overlaps the next pushes
/// it along, toward the notification area, a margin away, and never back toward the taskbar's start, where the app
/// buttons are. One that grows past the room the others leave toward the notification area's edge, or where the last was
/// dropped past it, shows smaller. Overlays that do not overlap stay where their snaps put them. A gesture or an edit that
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
    /// buttons, or over another. Null off a taskbar or alone on it, whatever its size, so an overlay alone need not measure
    /// itself to know it has all the room.
    /// </summary>
    public static Func<(int Width, int Height), int?>? RoomOf(
        IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown) =>
        Shared(overlays, index, screens, shown) ? size => Room(overlays, index, screens, size, shown) : null;

    /// <summary>
    /// The overlays after a gesture or an edit, with each one on a taskbar that shows pushed along by another, at the sizes
    /// <paramref name="shown"/> boxes them, given the place it shows at as its own offset: as one undo step with what pushed
    /// it, it then stays there when the one that pushed it hides, shrinks or moves away.
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
                && pushed.X != own.X)
            {
                settled[at] = overlays[at] with { Snap = overlays[at].Snapped! with { Offset = pushed.X - bar.Left } };
            }
        }

        return settled;
    }

    private static int Room(
        IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, (int Width, int Height) size, IReadOnlyList<PixelRect?> shown)
    {
        var (screen, bar, row) = Row(overlays, index, screens, size, shown)!.Value;
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var place = row.FindIndex(each => each.At == index);
        var after = row.Skip(place + 1).Sum(each => each.Width + gap);

        // One beside the notification area grows toward the taskbar's start, up to the one before it.
        var lefts = Along(row, gap);
        var from = overlays[index].Snapped?.Offset is not null ? lefts[place]
            : place > 0 ? lefts[place - 1] + row[place - 1].Width + gap
            : bar.Left;
        return End(row, screen, bar) - from - after;
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
        IReadOnlyList<PixelRect?> shown)
    {
        if (Snaps.OnTaskbar(overlays[index], screens) is not var (screen, bar))
        {
            return null;
        }

        var sizes = Enumerable.Range(0, overlays.Count)
            .Select(at => at == index ? size : at < shown.Count && shown[at] is { } box ? (box.Width, box.Height) : ((int Width, int Height)?)null)
            .ToList();
        // In the order they lie in, one beside the notification area by its edge there, so its growing never puts it before another.
        var end = Snaps.RightEnd(screen, bar);
        var row = Enumerable.Range(0, overlays.Count)
            .Where(at => sizes[at] is not null && On(overlays[at], screens, screen))
            .Select(at => (At: at, Left: Snaps.Locate(overlays[at], screens, sizes[at]!.Value.Width, sizes[at]!.Value.Height)!.X, sizes[at]!.Value.Width))
            .OrderBy(each => overlays[each.At].Snapped?.Offset is null ? end : each.Left)
            .ThenBy(each => each.At)
            .ToList();
        return (screen, bar, row);
    }

    // The last keeps to the notification area's edge, or to where it was dropped past it, and never leaves the taskbar.
    private static int End(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar) =>
        Math.Min(bar.Right, Math.Max(row[^1].Left + row[^1].Width, Snaps.RightEnd(screen, bar)));

    // Each pushed along by the one before it, toward the notification area only.
    private static int[] Along(List<(int At, int Left, int Width)> row, int gap)
    {
        var lefts = row.Select(each => each.Left).ToArray();
        for (var at = 1; at < row.Count; at++)
        {
            lefts[at] = Math.Max(lefts[at], lefts[at - 1] + row[at - 1].Width + gap);
        }

        return lefts;
    }

    // Overlays fitted to their room never pass the end; one that still would, as while another's new size has not shown
    // yet, is held at the end, and those before it back, so none covers another or leaves the taskbar.
    private static int[] Apart(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar)
    {
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var lefts = Along(row, gap);
        var end = End(row, screen, bar);
        lefts[^1] = Math.Min(lefts[^1], end - row[^1].Width);
        for (var at = row.Count - 2; at >= 0; at--)
        {
            lefts[at] = Math.Min(lefts[at], lefts[at + 1] - gap - row[at].Width);
        }

        return [.. lefts.Select(left => Math.Max(bar.Left, left))];
    }
}
