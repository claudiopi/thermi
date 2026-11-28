using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Tray;
using Thermi.Windows.Painting;

namespace Thermi.Windows.Tray;

/// <summary>
/// thermi's mark, a capsule creature peeking over a line, the taskbar's edge, at any size: Core's hand-placed pixels at 16
/// and 24 px, its vector (<see cref="MarkShape"/>) above. Each state has its gradient and eyes; stopped sensors and high
/// contrast draw the one-color glyph in <c>ink</c>, the body with its eyes cut out. The line is always in <c>ink</c>, the
/// taskbar's text color.
/// </summary>
internal static class MarkPainter
{
    public static void Draw(Graphics g, MarkState state, Rgb ink, RectangleF box)
    {
        var look = MarkLook.Of(state);
        if (Core.Painting.MarkPixels.For((int)box.Width) is { } pixels)
        {
            MarkCells.Draw(g, pixels, look, ink, box);
            return;
        }

        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(box.Left, box.Top);
        g.ScaleTransform(box.Width / MarkShape.Units, box.Height / MarkShape.Units);
        Vector(g, look, ink);
        g.Restore(saved);
    }

    private static void Vector(Graphics g, MarkLook look, Rgb ink)
    {
        using var body = Body();
        if (look.Stops is { } stops)
        {
            var (x, y, width, height) = MarkShape.Body;
            using var fill = new LinearGradientBrush(
                new RectangleF(x, y, width, height),
                PainterShapes.Drawn(stops[0].Color),
                PainterShapes.Drawn(stops[^1].Color),
                LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = [.. stops.Select(stop => PainterShapes.Drawn(stop.Color))],
                    Positions = [.. stops.Select(stop => stop.At)],
                },
                // Flipped rather than repeated, so an edge pixel past the gradient's box takes its own end's color.
                WrapMode = WrapMode.TileFlipX,
            };
            g.FillPath(fill, body);
            Eyes(g, look.Eyes, MarkLook.EyeInk);
        }
        else
        {
            // The eyes are holes: the body and the eyes in one path, filled alternately.
            foreach (var x in MarkShape.EyesAt)
            {
                using var eye = Eye(x, MarkEyes.Open);
                body.AddPath(eye, connect: false);
            }

            using var glyph = new SolidBrush(PainterShapes.Drawn(ink));
            g.FillPath(glyph, body);
        }

        var (from, to, at) = MarkShape.Line;
        using var line = new Pen(PainterShapes.Drawn(ink), MarkShape.Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, from, at, to, at);
    }

    private static GraphicsPath Body()
    {
        var (x, y, width, height) = MarkShape.Body;
        var (right, bottom, diameter) = (x + width, y + height, 2 * MarkShape.Shoulder);
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddLine(x, bottom, x, MarkShape.SidesTop);
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(right - diameter, y, diameter, diameter, 270, 90);
        path.AddLine(right, MarkShape.SidesTop, right, bottom);
        path.CloseFigure();
        return path;
    }

    private static void Eyes(Graphics g, MarkEyes eyes, Rgb color)
    {
        foreach (var x in MarkShape.EyesAt)
        {
            if (eyes == MarkEyes.Closed)
            {
                var lid = MarkShape.Lid(x);
                using var pen = new Pen(PainterShapes.Drawn(color), MarkShape.Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawBezier(pen, lid[0].X, lid[0].Y, lid[1].X, lid[1].Y, lid[2].X, lid[2].Y, lid[3].X, lid[3].Y);
                continue;
            }

            using var eye = Eye(x, eyes);
            using var brush = new SolidBrush(PainterShapes.Drawn(color));
            g.FillPath(brush, eye);
        }
    }

    private static GraphicsPath Eye(float x, MarkEyes eyes)
    {
        var (y, rx, ry) = MarkShape.Eye(eyes);
        var path = new GraphicsPath();
        path.AddEllipse(x - rx, y - ry, 2 * rx, 2 * ry);
        using var lean = new Matrix();
        lean.RotateAt(MarkShape.Lean, new PointF(x, MarkShape.LeanAt));
        path.Transform(lean);
        return path;
    }
}
