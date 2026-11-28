namespace Thermi.Core.Placement;

/// <summary>
/// The parts of a taskbar no overlay covers, <see cref="Screen.Start"/>, <see cref="Screen.Centre"/> and
/// <see cref="Screen.End"/>, and the rooms between them, a margin from each, where an overlay sits on it. An overlay that
/// fits in none shows beside the taskbar, as a vertical one does, and a move onto it is refused, as onto a full taskbar.
/// Windows' taskbar tells no parts, so all of it is one room.
/// </summary>
internal static class BarParts
{
    /// <summary>Whether an overlay that wide fits in a room of the taskbar.</summary>
    public static bool Fits(Screen screen, PixelRect bar, int width) => Beside(screen, bar, bar.Left, width) is not null;

    /// <summary>
    /// The room an overlay showing at <paramref name="box"/> lies in: the whole taskbar where it has no parts, and null off
    /// the taskbar, as for one beside it.
    /// </summary>
    public static (int Left, int Right)? Lane(Screen screen, PixelRect bar, PixelRect box) =>
        !Parts(screen).Any() ? (bar.Left, bar.Right)
        : box.Overlaps(bar) ? Gaps(screen, bar).Where(gap => box.Left >= gap.Left && box.Right <= gap.Right).Select(gap => ((int, int)?)gap).FirstOrDefault()
        : null;

    /// <summary>
    /// Where an overlay that would sit at <paramref name="left"/> goes: in the room that holds it nearest that, or null where
    /// none holds it, as on Plasma's panel, whose task manager fills what the others leave and tells no program where its
    /// buttons end.
    /// </summary>
    public static int? Beside(Screen screen, PixelRect bar, int left, int width) =>
        !Parts(screen).Any() ? left
        : Gaps(screen, bar)
            .Where(gap => gap.Right - gap.Left >= width)
            .Select(gap => (int?)Math.Clamp(left, gap.Left, gap.Right - width))
            .OrderBy(at => Math.Abs(at!.Value - left))
            .FirstOrDefault();

    private static IEnumerable<PixelRect> Parts(Screen screen) =>
        new[] { screen.Start, screen.Centre, screen.End }.OfType<PixelRect>().Where(part => part.Width > 0).OrderBy(part => part.Left);

    // From the taskbar's start to its end, the room before each part and after the last.
    private static IEnumerable<(int Left, int Right)> Gaps(Screen screen, PixelRect bar)
    {
        var margin = Snaps.Scaled(Snaps.Margin, screen);
        var from = bar.Left;
        foreach (var part in Parts(screen))
        {
            yield return (from, part.Left - margin);
            from = Math.Max(from, part.Right + margin);
        }

        yield return (from, bar.Right);
    }
}
