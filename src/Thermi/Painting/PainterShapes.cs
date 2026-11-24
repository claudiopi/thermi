using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.Painting;

/// <summary>What the painter draws besides text: the pills and the graphs. Sizes are in pixels at 100 %, times the scale.</summary>
internal static class PainterShapes
{
    public const float OuterPadding = 4;
    public const float GraphWidth = 30;

    /// <summary>The width of thermi's mark on the "Sensors stopped" pill.</summary>
    public const float MarkWidth = 16;

    private const float MeterHeight = 14;
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
    /// The last minute, a column a second with the newest at the right, each in its level's color. A missing second leaves
    /// its column empty, and a zero still shows a pixel, so a gap never reads as a zero.
    /// </summary>
    public static void Graph(Graphics g, IReadOnlyList<GraphPoint?> points, OverlayLook look, PointF at, float scale)
    {
        var height = MeterHeight * scale;
        var top = at.Y - (height / 2);
        var column = GraphWidth * scale / Seconds;
        using var off = new SolidBrush(Drawn(look.Pills.MeterOff));
        using var normal = new SolidBrush(Drawn(look.Normal));
        using var warning = new SolidBrush(Drawn(look.Warning));
        using var critical = new SolidBrush(Drawn(look.Critical));
        g.FillRectangle(off, at.X, top, GraphWidth * scale, height);
        var first = Seconds - points.Count;
        for (var second = Math.Max(0, -first); second < points.Count; second++)
        {
            if (points[second] is { } point)
            {
                var lit = Math.Max(scale, (float)point.Fill * height);
                var brush = point.Level switch { Level.Critical => critical, Level.Warning => warning, Level.Normal => normal, _ => normal };
                g.FillRectangle(brush, at.X + ((first + second) * column), top + height - lit, column, lit);
            }
        }
    }

    // A radius of half the height is the round pill, two half circles; none is a plain rectangle.
    private static GraphicsPath Rounded(RectangleF rect, float radius)
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
