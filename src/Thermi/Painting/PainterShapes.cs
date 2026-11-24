using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;

namespace Thermi.Painting;

/// <summary>What the painter draws besides text: the pills and the graphs. Sizes are in pixels at 100 %, times the scale.</summary>
internal static class PainterShapes
{
    public const float OuterPadding = 4;
    public const float GraphWidth = 24;
    public const float MeterWidth = 12;

    /// <summary>The width of thermi's mark on the "Sensors stopped" pill.</summary>
    public const float MarkWidth = 16;

    private const float GraphLine = 1.5f;

    // How much of its level's color a graph's fill takes, of 255: light, so the line reads first.
    private const int GraphFill = 77;
    private const float ReversedPadding = 3;
    private const float ReversedRadius = 3;
    private const float GlowWidth = 2;

    // How much of the critical color a glowing pill takes at the glow's height, of 255.
    private const int GlowTint = 90;

    private static readonly int Seconds = (int)ItemHistory.Kept.TotalSeconds;

    public static Color Drawn(Rgb color) => Color.FromArgb(color.R, color.G, color.B);

    /// <summary>A pill drawn opaque and flat: its color inside its border, with the look's corners.</summary>
    public static void Pill(Graphics g, OverlayLook look, RectangleF box, float scale)
    {
        var outer = new RectangleF(box.Left + 0.5f, box.Top + 0.5f, box.Width - 1, box.Height - 1);
        using var outerPath = Rounded(outer, (float)look.Radius(outer.Height));
        using var outerBrush = new SolidBrush(Drawn(look.Pills.Outer));
        using var border = new Pen(Drawn(look.Pills.Border), Math.Max(1, scale));
        g.FillPath(outerBrush, outerPath);
        g.DrawPath(border, outerPath);
    }

    /// <summary>
    /// The once-only glow of a pill whose item first went critical: its border and a tint in the critical color, at
    /// <paramref name="strength"/>, 0 to 1.
    /// </summary>
    public static void Glow(Graphics g, OverlayLook look, RectangleF box, float scale, double strength)
    {
        var inside = new RectangleF(box.Left + scale, box.Top + scale, box.Width - (2 * scale), box.Height - (2 * scale));
        using var path = Rounded(inside, (float)look.Radius(inside.Height));
        using var tint = new SolidBrush(Color.FromArgb((int)Math.Round(GlowTint * strength), Drawn(look.Critical)));
        using var ring = new Pen(Color.FromArgb((int)Math.Round(255 * strength), Drawn(look.Critical)), GlowWidth * scale);
        g.FillPath(tint, path);
        g.DrawPath(ring, path);
    }

    /// <summary>The box a critical value is drawn reversed on, a little wider than its text.</summary>
    public static void Reversed(Graphics g, Rgb color, RectangleF text, float scale)
    {
        var box = RectangleF.Inflate(text, ReversedPadding * scale, 0);
        using var path = Rounded(box, ReversedRadius * scale);
        using var brush = new SolidBrush(Drawn(color));
        g.FillPath(brush, path);
    }

    public static void Underline(Graphics g, Rgb color, RectangleF line)
    {
        using var brush = new SolidBrush(Drawn(color));
        g.FillRectangle(brush, line);
    }

    /// <summary>A dashed line over <paramref name="text"/>, 2 px at 100 %, which marks the CPU throttling apart from any level's mark.</summary>
    public static void Overline(Graphics g, Rgb color, RectangleF text, float scale)
    {
        using var pen = new Pen(Drawn(color), 2 * scale) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var y = text.Top - scale;
        g.DrawLine(pen, text.Left, y, text.Right, y);
    }

    /// <summary>thermi's mark at <paramref name="at"/>, its left middle: a small pill's outline with a full meter inside, in one color.</summary>
    public static void Mark(Graphics g, Rgb color, PointF at, float scale)
    {
        var height = MarkWidth * 5 / 8 * scale;
        var outline = new RectangleF(at.X + (scale / 2), at.Y - (height / 2), (MarkWidth * scale) - scale, height);
        var meter = RectangleF.Inflate(outline, -2 * scale, -2 * scale);
        using var outer = Rounded(outline, outline.Height / 2);
        using var inner = Rounded(meter, meter.Height / 2);
        using var pen = new Pen(Drawn(color), scale);
        using var brush = new SolidBrush(Drawn(color));
        g.DrawPath(pen, outer);
        g.FillPath(brush, inner);
    }

    /// <summary>
    /// A meter at <paramref name="at"/>, its left middle: <see cref="OverlayLayout.Segments"/> stacked a pixel apart, the
    /// lowest <paramref name="lit"/> in <paramref name="color"/> and the rest in the faint track's.
    /// </summary>
    public static void Meter(Graphics g, int lit, Rgb color, OverlayLook look, PointF at, float scale)
    {
        var (box, segment, gap) = Frame(at, MeterWidth, look, scale);
        using var on = new SolidBrush(Drawn(color));
        using var off = new SolidBrush(Drawn(look.Pills.MeterOff));
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        for (var fromBottom = 0; fromBottom < OverlayLayout.Segments; fromBottom++)
        {
            g.FillRectangle(fromBottom < lit ? on : off, box.Left, box.Bottom - ((fromBottom + 1) * segment) - (fromBottom * gap), box.Width, segment);
        }

        g.SmoothingMode = smoothing;
    }

    /// <summary>
    /// The last minute, newest at the right, as a line over a light fill, each second in its level's color, on the meter's
    /// track. A missing second breaks both, and a zero still shows the line, so a gap never reads as a zero.
    /// </summary>
    public static void Graph(Graphics g, IReadOnlyList<GraphPoint?> points, OverlayLook look, PointF at, float scale)
    {
        var (box, _, _) = Frame(at, GraphWidth, look, scale);
        using var off = new SolidBrush(Drawn(look.Pills.MeterOff));

        // Whole pixels then cover whole pixels, so the track's edges stay as sharp as the meter's.
        var offset = g.PixelOffsetMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(off, box);
        var first = Seconds - points.Count;
        var second = Math.Max(0, -first);
        while (second < points.Count)
        {
            second = points[second] is { } point ? Run(g, points, second, first, look.MeterColor(point.Level), box, GraphLine * scale) : second + 1;
        }

        g.PixelOffsetMode = offset;
    }

    // As tall as the value's text, on whole pixels so the segments' edges stay sharp; the graph takes the same height.
    private static (Rectangle Box, int Segment, int Gap) Frame(PointF at, float width, OverlayLook look, float scale)
    {
        const int Segments = OverlayLayout.Segments;
        var gap = Math.Max(1, (int)scale);
        var segment = Math.Max(1, (int)Math.Round(((look.Value.Size * scale) - (gap * (Segments - 1))) / Segments, MidpointRounding.AwayFromZero));
        var height = (segment * Segments) + (gap * (Segments - 1));
        var box = new Rectangle((int)Math.Round(at.X), (int)Math.Round(at.Y - (height / 2f)), (int)Math.Round(width * scale), height);
        return (box, segment, gap);
    }

    // A second is under a pixel wide, so the seconds of one level are filled as one shape and stroked as one line: seconds
    // drawn one by one blend at their shared edges into seams. The line runs on to the next second's height, so a change of
    // level never breaks it. Gives the second after the run.
    private static int Run(Graphics g, IReadOnlyList<GraphPoint?> points, int start, int first, Rgb color, Rectangle box, float line)
    {
        var column = (float)box.Width / Seconds;
        var tops = new List<PointF>();
        var second = start;
        for (; second < points.Count && points[second] is { } point && point.Level == points[start]!.Value.Level; second++)
        {
            var (left, top) = (box.Left + ((first + second) * column), Top(point, box, line));
            tops.Add(new PointF(left, top));
            tops.Add(new PointF(left + column, top));
        }

        using var fill = new SolidBrush(Color.FromArgb(GraphFill, Drawn(color)));
        g.FillPolygon(fill, [new PointF(tops[0].X, box.Bottom), .. tops, new PointF(tops[^1].X, box.Bottom)]);
        if (second < points.Count && points[second] is { } next)
        {
            tops.Add(new PointF(tops[^1].X, Top(next, box, line)));
        }

        using var pen = new Pen(Drawn(color), line) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, [.. tops]);
        return second;
    }

    private static float Top(GraphPoint point, Rectangle box, float line) => box.Bottom - (line / 2) - ((float)point.Fill * (box.Height - line));

    /// <summary>A radius of half the height is the round pill, two half circles; none is a plain rectangle.</summary>
    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        if (radius <= 0)
        {
            path.AddRectangle(rect);
        }
        else if (diameter >= rect.Height)
        {
            path.AddArc(rect.X, rect.Y, rect.Height, rect.Height, 90, 180);
            path.AddArc(rect.Right - rect.Height, rect.Y, rect.Height, rect.Height, 270, 180);
        }
        else
        {
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        }

        path.CloseFigure();
        return path;
    }
}
