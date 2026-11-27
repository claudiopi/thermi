using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Core.Painting;

/// <summary>One level's run of a graph's seconds: the shape its light fill covers, and its line, which runs on to the next second.</summary>
public sealed record GraphRun(Level Level, IReadOnlyList<(double X, double Y)> Fill, IReadOnlyList<(double X, double Y)> Line);

/// <summary>
/// Where a painter draws what is no text, in the picture's pixels: a meter's segments, a graph's track and runs, and
/// thermi's mark. Every painter draws them from here, so a meter or a graph has one shape on every OS.
/// </summary>
public static class MeterShapes
{
    private static readonly int Seconds = (int)ItemHistory.Kept.TotalSeconds;

    /// <summary>
    /// A meter's or a graph's box at <paramref name="x"/>, <paramref name="y"/>, its left middle, <paramref name="width"/>
    /// unscaled pixels wide: as tall as the value's text and as much wider as it is larger, on whole pixels so the segments'
    /// edges stay sharp; with a segment's height and the gap between two.
    /// </summary>
    public static (PixelRect Box, int Segment, int Gap) Frame(double x, double y, double width, OverlayLook look, double scale)
    {
        const int Segments = OverlayLayout.Segments;
        var gap = Math.Max(1, (int)scale);
        var segment = Math.Max(1, (int)Math.Round(((look.Value.Size * scale) - (gap * (Segments - 1))) / Segments, MidpointRounding.AwayFromZero));
        var height = (segment * Segments) + (gap * (Segments - 1));
        var (left, top) = ((int)Math.Round(x), (int)Math.Round(y - (height / 2.0)));
        return (new PixelRect(left, top, left + (int)Math.Round(width * look.MeterStretch * scale), top + height), segment, gap);
    }

    /// <summary>A meter's <see cref="OverlayLayout.Segments"/>, from the bottom up, a pixel apart.</summary>
    public static IReadOnlyList<PixelRect> Segments(double x, double y, OverlayLook look, double scale)
    {
        var (box, segment, gap) = Frame(x, y, PaintSizes.MeterWidth, look, scale);
        return [.. Enumerable.Range(0, OverlayLayout.Segments).Select(fromBottom =>
        {
            var top = box.Bottom - ((fromBottom + 1) * segment) - (fromBottom * gap);
            return new PixelRect(box.Left, top, box.Right, top + segment);
        })];
    }

    /// <summary>
    /// A graph's track at <paramref name="x"/>, <paramref name="y"/>, and the last minute on it, newest at the right, as a
    /// run per level. A second is under a pixel wide, so the seconds of one level are one shape and one line: seconds drawn
    /// one by one blend at their shared edges into seams. A missing second breaks both, and a zero still shows the line, so
    /// a gap never reads as a zero.
    /// </summary>
    public static (PixelRect Track, IReadOnlyList<GraphRun> Runs) Graph(IReadOnlyList<GraphPoint?> points, double x, double y, OverlayLook look, double scale)
    {
        var (box, _, _) = Frame(x, y, PaintSizes.GraphWidth, look, scale);
        var line = PaintSizes.GraphLine * scale;
        var first = Seconds - points.Count;
        var runs = new List<GraphRun>();
        var second = Math.Max(0, -first);
        while (second < points.Count)
        {
            if (points[second] is null)
            {
                second++;
                continue;
            }

            var (run, next) = Run(points, second, first, box, line);
            runs.Add(run);
            second = next;
        }

        return (box, runs);
    }

    /// <summary>thermi's mark at its left middle: a small pill's outline, and the full meter inside it.</summary>
    public static (PillBox Outline, PillBox Meter) Mark(double x, double y, double scale)
    {
        var height = PaintSizes.MarkWidth * 5 / 8 * scale;
        var outline = new PillBox(x + (scale / 2), y - (height / 2), (PaintSizes.MarkWidth * scale) - scale, height);
        var inset = 2 * scale;
        return (outline, new PillBox(outline.Left + inset, outline.Top + inset, outline.Width - (2 * inset), outline.Height - (2 * inset)));
    }

    // The line runs on to the next second's height, so a change of level never breaks it. Gives the second after the run.
    private static (GraphRun Run, int Next) Run(IReadOnlyList<GraphPoint?> points, int start, int first, PixelRect box, double line)
    {
        var column = (double)box.Width / Seconds;
        var level = points[start]!.Value.Level;
        var tops = new List<(double X, double Y)>();
        var second = start;
        for (; second < points.Count && points[second] is { } point && point.Level == level; second++)
        {
            var (left, top) = (box.Left + ((first + second) * column), Top(point, box, line));
            tops.Add((left, top));
            tops.Add((left + column, top));
        }

        List<(double X, double Y)> fill = [(tops[0].X, box.Bottom), .. tops, (tops[^1].X, box.Bottom)];
        if (second < points.Count && points[second] is { } next)
        {
            tops.Add((tops[^1].X, Top(next, box, line)));
        }

        return (new GraphRun(level, fill, tops), second);
    }

    private static double Top(GraphPoint point, PixelRect box, double line) => box.Bottom - (line / 2) - (point.Fill * (box.Height - line));
}
