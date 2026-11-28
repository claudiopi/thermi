using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Tray;

namespace Thermi.Linux.Painting;

/// <summary>
/// thermi's mark with Cairo, as Windows' painter draws it with GDI+: Core's hand-placed pixels at 16 and 24 px, its vector
/// (<see cref="MarkShape"/>) above, the one-color glyph with its eyes cut out in <c>ink</c>, and the line always in <c>ink</c>.
/// </summary>
internal static class MarkPainter
{
    /// <summary>Draws the mark in <paramref name="state"/> filling a square of <paramref name="size"/> pixels at the context's origin.</summary>
    public static void Draw(Cairo.Context cr, MarkState state, Rgb ink, int size)
    {
        var look = MarkLook.Of(state);
        if (MarkPixels.For(size) is { } pixels)
        {
            Cells(cr, pixels, look, ink, size);
            return;
        }

        cr.Save();
        cr.Scale(size / MarkShape.Units, size / MarkShape.Units);
        Vector(cr, look, ink);
        cr.Restore();
    }

    private static void Cells(Cairo.Context cr, MarkPixels pixels, MarkLook look, Rgb ink, int size)
    {
        var offset = (size - pixels.Size) / 2;
        cr.Save();
        cr.Antialias = Cairo.Antialias.None;
        foreach (var cell in pixels.Body(look))
        {
            cr.Rectangle(offset + cell.X, offset + cell.Y, 1, 1);
            PainterShapes.Source(cr, cell.Color ?? ink);
            cr.Fill();
        }

        var (y, from, to) = pixels.Line;
        cr.Rectangle(offset + from, offset + y, to - from + 1, 1);
        PainterShapes.Source(cr, ink);
        cr.Fill();
        cr.Restore();
    }

    private static void Vector(Cairo.Context cr, MarkLook look, Rgb ink)
    {
        Body(cr);
        if (look.Stops is { } stops)
        {
            var (x, _, width, _) = MarkShape.Body;
            using var fill = new Cairo.LinearGradient(x, 0, x + width, 0);
            foreach (var (at, color) in stops)
            {
                fill.AddColorStopRgb(at, color.R / 255.0, color.G / 255.0, color.B / 255.0);
            }

            // Reflected rather than repeated, as Windows' flips it, so a pixel past the gradient takes its own end's color.
            fill.Extend = Cairo.Extend.Reflect;
            cr.SetSource(fill);
            cr.Fill();
            Eyes(cr, look.Eyes);
        }
        else
        {
            // The eyes are holes: the body and the eyes in one path, filled even-odd.
            foreach (var x in MarkShape.EyesAt)
            {
                Eye(cr, x, MarkEyes.Open);
            }

            cr.FillRule = Cairo.FillRule.EvenOdd;
            PainterShapes.Source(cr, ink);
            cr.Fill();
            cr.FillRule = Cairo.FillRule.Winding;
        }

        var (lineFrom, lineTo, lineY) = MarkShape.Line;
        cr.MoveTo(lineFrom, lineY);
        cr.LineTo(lineTo, lineY);
        Stroked(cr, ink);
    }

    private static void Body(Cairo.Context cr)
    {
        var (x, y, width, height) = MarkShape.Body;
        var radius = MarkShape.Shoulder;
        cr.NewPath();
        cr.MoveTo(x, y + height);
        cr.LineTo(x, MarkShape.SidesTop);
        cr.Arc(x + radius, y + radius, radius, Math.PI, 1.5 * Math.PI);
        cr.Arc(x + width - radius, y + radius, radius, 1.5 * Math.PI, 2 * Math.PI);
        cr.LineTo(x + width, y + height);
        cr.ClosePath();
    }

    private static void Eyes(Cairo.Context cr, MarkEyes eyes)
    {
        foreach (var x in MarkShape.EyesAt)
        {
            cr.NewPath();
            if (eyes == MarkEyes.Closed)
            {
                var lid = MarkShape.Lid(x);
                cr.MoveTo(lid[0].X, lid[0].Y);
                cr.CurveTo(lid[1].X, lid[1].Y, lid[2].X, lid[2].Y, lid[3].X, lid[3].Y);
                Stroked(cr, MarkLook.EyeInk);
                continue;
            }

            Eye(cr, x, eyes);
            PainterShapes.Source(cr, MarkLook.EyeInk);
            cr.Fill();
        }
    }

    // An upright oval leaning to the right about its height, added to the path as one closed figure of its own.
    private static void Eye(Cairo.Context cr, float x, MarkEyes eyes)
    {
        var (y, rx, ry) = MarkShape.Eye(eyes);
        cr.Save();
        cr.Translate(x, MarkShape.LeanAt);
        cr.Rotate(MarkShape.Lean * Math.PI / 180);
        cr.Translate(0, y - MarkShape.LeanAt);
        cr.Scale(rx, ry);
        cr.NewSubPath();
        cr.Arc(0, 0, 1, 0, 2 * Math.PI);
        cr.ClosePath();
        cr.Restore();
    }

    private static void Stroked(Cairo.Context cr, Rgb color)
    {
        cr.LineWidth = MarkShape.Stroke;
        cr.LineCap = Cairo.LineCap.Round;
        PainterShapes.Source(cr, color);
        cr.Stroke();
    }
}
