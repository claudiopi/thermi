using Thermi.Core.Look;
using Thermi.Core.Painting;

namespace Thermi.Linux.Painting;

/// <summary>
/// What the painter draws besides text, with Cairo: the pills, meters and graphs, where Core's <see cref="MeterShapes"/>
/// puts them, as Windows' painter draws them with GDI+. Sizes are in pixels at 100 %, times the scale.
/// </summary>
internal static class PainterShapes
{
    public static void Source(Cairo.Context cr, Rgb color, double alpha = 1) => cr.SetSourceRgba(color.R / 255.0, color.G / 255.0, color.B / 255.0, alpha);

    /// <summary>The line a pill at <paramref name="box"/> is drawn along: half a pixel in, so its border falls on whole pixels.</summary>
    public static PillBox Outline(PillBox box) => new(box.Left + 0.5, box.Top + 0.5, box.Width - 1, box.Height - 1);

    /// <summary>
    /// A pill drawn opaque and flat: its color inside its border, with the look's corners, those of a pill
    /// <paramref name="row"/> tall at most, so a standing bar curves as the pills it holds.
    /// </summary>
    public static void Pill(Cairo.Context cr, OverlayLook look, PillBox box, double scale, double row)
    {
        Rounded(cr, Outline(box), look.Radius(Math.Min(box.Height, row) - 1));
        Source(cr, look.Pills.Outer);
        cr.FillPreserve();
        Source(cr, look.Pills.Border);
        cr.LineWidth = Math.Max(1, scale);
        cr.Stroke();
    }

    /// <summary>A pill a single bar holds; where its color is the bar's, as under high contrast, a border sets it apart.</summary>
    public static void InnerPill(Cairo.Context cr, OverlayLook look, PillBox box, double scale)
    {
        var padding = PaintSizes.OuterPadding * scale;
        Rounded(cr, box, look.InnerRadius(box.Height + (2 * padding), padding));
        Source(cr, look.Pills.Inner);
        if (look.Pills.Inner != look.Pills.Outer)
        {
            cr.Fill();
            return;
        }

        cr.FillPreserve();
        Source(cr, look.Pills.Border);
        cr.LineWidth = Math.Max(1, scale);
        cr.Stroke();
    }

    /// <summary>The once-only glow of a pill whose item first went critical, at <paramref name="strength"/>, 0 to 1.</summary>
    public static void Glow(Cairo.Context cr, OverlayLook look, PillBox box, double scale, double strength)
    {
        var inside = new PillBox(box.Left + scale, box.Top + scale, box.Width - (2 * scale), box.Height - (2 * scale));
        Rounded(cr, inside, look.Radius(inside.Height));
        Source(cr, look.Critical, Math.Round(PaintSizes.GlowTint * strength) / 255);
        cr.FillPreserve();
        Source(cr, look.Critical, Math.Round(255 * strength) / 255);
        cr.LineWidth = PaintSizes.GlowWidth * scale;
        cr.Stroke();
    }

    /// <summary>The box a critical value is drawn reversed on, a little wider than its text.</summary>
    public static void Reversed(Cairo.Context cr, Rgb color, PillBox text, double scale)
    {
        var padding = PaintSizes.ReversedPadding * scale;
        Rounded(cr, text with { Left = text.Left - padding, Width = text.Width + (2 * padding) }, PaintSizes.ReversedRadius * scale);
        Source(cr, color);
        cr.Fill();
    }

    public static void Underline(Cairo.Context cr, Rgb color, PillBox line)
    {
        cr.Rectangle(line.Left, line.Top, line.Width, line.Height);
        Source(cr, color);
        cr.Fill();
    }

    /// <summary>A dashed line over <paramref name="text"/>, 2 px at 100 %, which marks the CPU throttling apart from any level's mark.</summary>
    public static void Overline(Cairo.Context cr, Rgb color, PillBox text, double scale)
    {
        var width = 2 * scale;
        cr.Save();
        cr.SetDash([3 * width, width], 0);
        cr.LineWidth = width;
        cr.MoveTo(text.Left, text.Top - scale);
        cr.LineTo(text.Right, text.Top - scale);
        Source(cr, color);
        cr.Stroke();
        cr.Restore();
    }

    /// <summary>thermi's mark at its left middle: a small pill's outline with a full meter inside, in one color.</summary>
    public static void Mark(Cairo.Context cr, Rgb color, double x, double y, double scale)
    {
        var (outline, meter) = MeterShapes.Mark(x, y, scale);
        Source(cr, color);
        Rounded(cr, outline, outline.Height / 2);
        cr.LineWidth = scale;
        cr.Stroke();
        Rounded(cr, meter, meter.Height / 2);
        cr.Fill();
    }

    /// <summary>A meter at its left middle: the lowest <paramref name="lit"/> segments in <paramref name="color"/>, the rest in the faint track's.</summary>
    public static void Meter(Cairo.Context cr, int lit, Rgb color, OverlayLook look, double x, double y, double scale)
    {
        var segments = MeterShapes.Segments(x, y, look, scale);
        cr.Save();
        cr.Antialias = Cairo.Antialias.None;
        for (var fromBottom = 0; fromBottom < segments.Count; fromBottom++)
        {
            var segment = segments[fromBottom];
            cr.Rectangle(segment.Left, segment.Top, segment.Width, segment.Height);
            Source(cr, fromBottom < lit ? color : look.Pills.MeterOff);
            cr.Fill();
        }

        cr.Restore();
    }

    /// <summary>The last minute on the meter's track, as a line over a light fill, each second in its level's color.</summary>
    public static void Graph(Cairo.Context cr, IReadOnlyList<GraphPoint?> points, OverlayLook look, double x, double y, double scale)
    {
        var (track, runs) = MeterShapes.Graph(points, x, y, look, scale);
        cr.Rectangle(track.Left, track.Top, track.Width, track.Height);
        Source(cr, look.Pills.MeterOff);
        cr.Fill();
        foreach (var run in runs)
        {
            var color = look.MeterColor(run.Level);
            Polygon(cr, run.Fill, close: true);
            Source(cr, color, PaintSizes.GraphFill / 255.0);
            cr.Fill();
            Polygon(cr, run.Line, close: false);
            Source(cr, color);
            cr.LineWidth = PaintSizes.GraphLine * scale;
            cr.LineJoin = Cairo.LineJoin.Round;
            cr.Stroke();
        }
    }

    /// <summary>A path of the box: a radius of half its height is the round pill, two half circles; none is a plain rectangle.</summary>
    public static void Rounded(Cairo.Context cr, PillBox box, double radius)
    {
        cr.NewPath();
        if (radius <= 0)
        {
            cr.Rectangle(box.Left, box.Top, box.Width, box.Height);
            return;
        }

        var half = Math.PI / 2;
        if (radius * 2 >= box.Height)
        {
            var round = box.Height / 2;
            cr.Arc(box.Left + round, box.Top + round, round, half, 3 * half);
            cr.Arc(box.Right - round, box.Top + round, round, -half, half);
        }
        else
        {
            cr.Arc(box.Left + radius, box.Top + radius, radius, Math.PI, 3 * half);
            cr.Arc(box.Right - radius, box.Top + radius, radius, -half, 0);
            cr.Arc(box.Right - radius, box.Bottom - radius, radius, 0, half);
            cr.Arc(box.Left + radius, box.Bottom - radius, radius, half, Math.PI);
        }

        cr.ClosePath();
    }

    private static void Polygon(Cairo.Context cr, IReadOnlyList<(double X, double Y)> points, bool close)
    {
        cr.NewPath();
        cr.MoveTo(points[0].X, points[0].Y);
        foreach (var (x, y) in points.Skip(1))
        {
            cr.LineTo(x, y);
        }

        if (close)
        {
            cr.ClosePath();
        }
    }
}
