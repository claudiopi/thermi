using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>
/// Where an overlay on a taskbar spot sits in its taskbar's row: the box of <see cref="Width"/> pixels from
/// <see cref="Left"/> that it keeps, or no <see cref="Left"/> when no place on that taskbar has room for it.
/// </summary>
public readonly record struct RowPlace(int? Left, int Width);

/// <summary>
/// The overlays a dragged one is laid among on the taskbars: all of them, the dragged one at <see cref="Overlay"/>, and
/// the width each keeps on a taskbar, null for one not shown there.
/// </summary>
public sealed record RowContext(IReadOnlyList<OverlayConfig> Overlays, int Overlay, IReadOnlyList<int?> Widths);

/// <summary>
/// The overlays snapped to one taskbar, laid out in the room its app buttons and notification area leave, a margin
/// apart, so none covers a button or another overlay. Each keeps the width of its values at their widest, so the row
/// stays put as values change. In the settings' order, each takes the place with room nearest its spot: the leftmost
/// for the left, the rightmost for the right, the one nearest the taskbar's middle for the center, the right one on a
/// tie. An overlay without room at its spot so takes the nearest place that has it, and one with room nowhere has none.
/// </summary>
public static class TaskbarRow
{
    /// <summary>
    /// The place of overlay <paramref name="index"/> in its taskbar's row, laid out with the others on that taskbar whose
    /// width is known; null when it is not shown on a taskbar's spot or its own width is unknown.
    /// </summary>
    public static RowPlace? Place(IReadOnlyList<OverlayConfig> overlays, int index, IReadOnlyList<Screen> screens, IReadOnlyList<int?> widths)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentNullException.ThrowIfNull(widths);
        if (index < 0 || index >= overlays.Count || index >= widths.Count || widths[index] is not { } width || On(overlays[index], screens) is not { } own)
        {
            return null;
        }

        var row = Enumerable.Range(0, Math.Min(overlays.Count, widths.Count))
            .Select(each => (Overlay: each, Width: widths[each], On: On(overlays[each], screens)))
            .Where(each => each.Width is not null && each.On?.Screen.Id == own.Screen.Id)
            .Select(each => (each.Overlay, each.On!.Value.Spot, each.Width!.Value));
        return new RowPlace(Lay(own.Screen, row).TryGetValue(index, out var left) ? left : null, width);
    }

    /// <summary>Whether the overlay shows on a taskbar's spot, and so keeps room in its row.</summary>
    public static bool Holds(OverlayConfig overlay, IReadOnlyList<Screen> screens) => On(overlay, screens) is not null;

    // The screen and taskbar spot an overlay shows at, or null for one free, in a corner, or on a monitor whose taskbar offers none.
    private static (Screen Screen, SnapSpot Spot)? On(OverlayConfig overlay, IReadOnlyList<Screen> screens)
    {
        if (overlay.Snapped is not { } snap || Snaps.ScreenOf(snap, screens) is not { } screen)
        {
            return null;
        }

        var spot = Snaps.Shown(snap.Spot, screen, overlay.Style.Orientation);
        return spot is SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight ? (screen, spot) : null;
    }

    private static Dictionary<int, int> Lay(Screen screen, IEnumerable<(int Overlay, SnapSpot Spot, int Width)> row)
    {
        var bar = Snaps.TaskbarOf(screen)!.Value;
        var margin = Snaps.Scaled(Snaps.Margin, screen);
        var free = Room(screen, bar, margin);
        var laid = new Dictionary<int, int>();
        foreach (var (overlay, spot, width) in row)
        {
            var wanted = spot == SnapSpot.TaskbarLeft ? bar.Left
                : spot == SnapSpot.TaskbarRight ? bar.Right - width
                : bar.Left + ((bar.Width - width) / 2);
            var places = free.Where(room => room.Right - room.Left >= width).Select(room => Math.Clamp(wanted, room.Left, room.Right - width)).ToList();
            if (places.Count == 0)
            {
                continue;
            }

            var left = places.OrderBy(place => Math.Abs(place - wanted)).ThenByDescending(place => place).First();
            laid[overlay] = left;
            free = [.. free.SelectMany(room => Taken(room, left, width, margin))];
        }

        return laid;
    }

    // From the taskbar's start to the notification area, a margin in from each, less the app buttons and a margin around them.
    private static List<(int Left, int Right)> Room(Screen screen, PixelRect bar, int margin)
    {
        var (start, end) = (bar.Left + margin, Snaps.RightEnd(screen, bar));
        List<(int Left, int Right)> room = screen.Apps is { } apps && apps.Right > start && apps.Left < end
            ? [(start, apps.Left - margin), (apps.Right + margin, end)]
            : [(start, end)];
        return [.. room.Where(each => each.Right > each.Left)];
    }

    private static IEnumerable<(int Left, int Right)> Taken((int Left, int Right) room, int left, int width, int margin) =>
        left < room.Left || left >= room.Right
            ? [room]
            : new[] { (room.Left, left - margin), (left + width + margin, room.Right) }.Where(each => each.Item2 > each.Item1);
}
