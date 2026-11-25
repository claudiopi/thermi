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

        var sizes = Enumerable.Range(0, overlays.Count)
            .Select(at => at == index ? size : at < shown.Count && shown[at] is { } box ? (box.Width, box.Height) : ((int Width, int Height)?)null)
            .ToList();

        if (Snaps.OnTaskbar(overlays[index], screens) is not var (screen, bar))
        {
            return own;
        }

        var row = Enumerable.Range(0, overlays.Count)
            .Where(at => sizes[at] is not null && Snaps.OnTaskbar(overlays[at], screens) is var (other, _) && other.Id == screen.Id)
            .Select(at => (At: at, Left: Snaps.Locate(overlays[at], screens, sizes[at]!.Value.Width, sizes[at]!.Value.Height)!.X, sizes[at]!.Value.Width))
            .OrderBy(each => each.Left)
            .ThenBy(each => each.At)
            .ToList();
        var lefts = Apart(row, screen, bar);
        var place = row.FindIndex(each => each.At == index);
        return own with { X = own.X + lefts[place] - row[place].Left };
    }

    private static int[] Apart(List<(int At, int Left, int Width)> row, Screen screen, PixelRect bar)
    {
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var lefts = row.Select(each => each.Left).ToArray();
        for (var at = 1; at < row.Count; at++)
        {
            lefts[at] = lefts[at] < lefts[at - 1] + row[at - 1].Width ? lefts[at - 1] + row[at - 1].Width + gap : lefts[at];
        }

        var end = Math.Min(bar.Right, Math.Max(row[^1].Left + row[^1].Width, Snaps.RightEnd(screen, bar)));
        lefts[^1] = Math.Min(lefts[^1], end - row[^1].Width);
        for (var at = row.Count - 2; at >= 0; at--)
        {
            lefts[at] = lefts[at] + row[at].Width > lefts[at + 1] ? lefts[at + 1] - gap - row[at].Width : lefts[at];
        }

        return [.. lefts.Select(left => Math.Max(bar.Left, left))];
    }
}
