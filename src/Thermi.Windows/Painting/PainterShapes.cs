using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Painting;

namespace Thermi.Windows.Painting;

/// <summary>
/// What the painter draws besides text, with GDI+: the pills, meters and graphs, where Core's <see cref="MeterShapes"/>
/// puts them. Sizes are in pixels at 100 %, times the scale.
/// </summary>
internal static class PainterShapes
{
    public static Color Drawn(Rgb color) => Color.FromArgb(color.R, color.G, color.B);

    /// <summary>
    /// A pill drawn opaque and flat: its color inside its border, with the look's corners, those of a pill
    /// <paramref name="row"/> tall at most, so a standing bar curves as the pills it holds.
    /// </summary>
    public static void Pill(Graphics g, OverlayLook look, RectangleF box, float scale, float row)
    {
        var outer = new RectangleF(box.Left + 0.5f, box.Top + 0.5f, box.Width - 1, box.Height - 1);
        using var outerPath = Rounded(outer, (float)look.Radius(Math.Min(box.Height, row) - 1));
        using var outerBrush = new SolidBrush(Drawn(look.Pills.Outer));
        using var border = new Pen(Drawn(look.Pills.Border), Math.Max(1, scale));
        g.FillPath(outerBrush, outerPath);
        g.DrawPath(border, outerPath);
    }

    /// <summary>A pill a single bar holds; where its color is the bar's, as under high contrast, a border sets it apart.</summary>
    public static void InnerPill(Graphics g, OverlayLook look, RectangleF box, float scale)
    {
        var padding = PaintSizes.OuterPadding * scale;
        using var path = Rounded(box, (float)look.InnerRadius(box.Height + (2 * padding), padding));
        using var brush = new SolidBrush(Drawn(look.Pills.Inner));
        g.FillPath(brush, path);
        if (look.Pills.Inner == look.Pills.Outer)
        {
            using var border = new Pen(Drawn(look.Pills.Border), Math.Max(1, scale));
            g.DrawPath(border, path);
        }
    }

    /// <summary>
    /// The once-only glow of a pill whose item first went critical: its border and a tint in the critical color, at
    /// <paramref name="strength"/>, 0 to 1.
    /// </summary>
    public static void Glow(Graphics g, OverlayLook look, RectangleF box, float scale, double strength)
    {
        var inside = new RectangleF(box.Left + scale, box.Top + scale, box.Width - (2 * scale), box.Height - (2 * scale));
        using var path = Rounded(inside, (float)look.Radius(inside.Height));
        using var tint = new SolidBrush(Color.FromArgb((int)Math.Round(PaintSizes.GlowTint * strength), Drawn(look.Critical)));
        using var ring = new Pen(Color.FromArgb((int)Math.Round(255 * strength), Drawn(look.Critical)), PaintSizes.GlowWidth * scale);
        g.FillPath(tint, path);
        g.DrawPath(ring, path);
    }

    /// <summary>The box a critical value is drawn reversed on, a little wider than its text.</summary>
    public static void Reversed(Graphics g, Rgb color, RectangleF text, float scale)
    {
        var box = RectangleF.Inflate(text, PaintSizes.ReversedPadding * scale, 0);
        using var path = Rounded(box, PaintSizes.ReversedRadius * scale);
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
        using var pen = new Pen(Drawn(color), 2 * scale) { DashStyle = DashStyle.Dash };
        var y = text.Top - scale;
        g.DrawLine(pen, text.Left, y, text.Right, y);
    }

    /// <summary>thermi's mark at <paramref name="at"/>, its left middle: a small pill's outline with a full meter inside, in one color.</summary>
    public static void Mark(Graphics g, Rgb color, PointF at, float scale)
    {
        var (outline, meter) = MeterShapes.Mark(at.X, at.Y, scale);
        using var outer = Rounded(outline.Rect(), (float)outline.Height / 2);
        using var inner = Rounded(meter.Rect(), (float)meter.Height / 2);
        using var pen = new Pen(Drawn(color), scale);
        using var brush = new SolidBrush(Drawn(color));
        g.DrawPath(pen, outer);
        g.FillPath(brush, inner);
    }

    /// <summary>A meter at <paramref name="at"/>, its left middle: the lowest <paramref name="lit"/> segments in <paramref name="color"/> and the rest in the faint track's.</summary>
    public static void Meter(Graphics g, int lit, Rgb color, OverlayLook look, PointF at, float scale)
    {
        using var on = new SolidBrush(Drawn(color));
        using var off = new SolidBrush(Drawn(look.Pills.MeterOff));
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        var segments = MeterShapes.Segments(at.X, at.Y, look, scale);
        for (var fromBottom = 0; fromBottom < segments.Count; fromBottom++)
        {
            g.FillRectangle(fromBottom < lit ? on : off, segments[fromBottom].Rect());
        }

        g.SmoothingMode = smoothing;
    }

    /// <summary>The last minute on the meter's track, as a line over a light fill, each second in its level's color.</summary>
    public static void Graph(Graphics g, IReadOnlyList<GraphPoint?> points, OverlayLook look, PointF at, float scale)
    {
        var (track, runs) = MeterShapes.Graph(points, at.X, at.Y, look, scale);
        using var off = new SolidBrush(Drawn(look.Pills.MeterOff));

        // Whole pixels then cover whole pixels, so the track's edges stay as sharp as the meter's.
        var offset = g.PixelOffsetMode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.FillRectangle(off, track.Rect());
        foreach (var run in runs)
        {
            var color = Drawn(look.MeterColor(run.Level));
            using var fill = new SolidBrush(Color.FromArgb(PaintSizes.GraphFill, color));
            g.FillPolygon(fill, run.Fill.Points());
            using var pen = new Pen(color, PaintSizes.GraphLine * scale) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, run.Line.Points());
        }

        g.PixelOffsetMode = offset;
    }

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
