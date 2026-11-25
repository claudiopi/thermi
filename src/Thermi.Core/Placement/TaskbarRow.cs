using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// Overlays that share a horizontal taskbar, laid out together so none covers another: one that overlaps the next pushes
/// it along, a margin away, and the last keeps to the notification area's edge, or to where it was dropped past it,
/// pushing back the ones before it. Overlays that do not overlap stay where their snaps put them.
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
    /// at: from the taskbar's start to where the last may end, less theirs and a margin between each. An overlay that grows
    /// past it shows at this width, so it never pushes another off the taskbar or covers it. Null off a taskbar or alone on
    /// it, whatever its size, so an overlay alone need not measure itself to know it has all the room.
    /// </summary>
    public static Func<(int Width, int Height), int?>? RoomOf(
        IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown) =>
        Shared(overlays, index, screens, shown) ? size => Room(overlays, index, screens, size, shown) : null;

    private static int Room(
        IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, (int Width, int Height) size, IReadOnlyList<PixelRect?> shown)
    {
        var (screen, bar, row) = Row(overlays, index, screens, size, shown)!.Value;
        var others = row.Where(each => each.At != index).ToList();
        return End(row, screen, bar) - bar.Left - others.Sum(each => each.Width) - (others.Count * Snaps.Scaled(Snaps.Margin, screen));
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
        var row = Enumerable.Range(0, overlays.Count)
            .Where(at => sizes[at] is not null && On(overlays[at], screens, screen))
            .Select(at => (At: at, Left: Snaps.Locate(overlays[at], screens, sizes[at]!.Value.Width, sizes[at]!.Value.Height)!.X, sizes[at]!.Value.Width))
            .OrderBy(each => each.Left)
            .ThenBy(each => each.At)
            .ToList();
        return (screen, bar, row);
    }

    // The last keeps to the notification area's edge, or to where it was dropped past it, and never leaves the taskbar.
    private static int End(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar) =>
        Math.Min(bar.Right, Math.Max(row[^1].Left + row[^1].Width, Snaps.RightEnd(screen, bar)));

    private static int[] Apart(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar)
    {
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var lefts = row.Select(each => each.Left).ToArray();
        for (var at = 1; at < row.Count; at++)
        {
            lefts[at] = lefts[at] < lefts[at - 1] + row[at - 1].Width ? lefts[at - 1] + row[at - 1].Width + gap : lefts[at];
        }

        var end = End(row, screen, bar);
        lefts[^1] = Math.Min(lefts[^1], end - row[^1].Width);
        for (var at = row.Count - 2; at >= 0; at--)
        {
            lefts[at] = lefts[at] + row[at].Width > lefts[at + 1] ? lefts[at + 1] - gap - row[at].Width : lefts[at];
        }

        return [.. lefts.Select(left => Math.Max(bar.Left, left))];
    }
}
