using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Tray;
using Thermi.Painting;

namespace Thermi.Tray;

/// <summary>The mark's eyes: closed asleep, open awake, wide alarmed.</summary>
internal enum MarkEyes
{
    Open,
    Wide,
    Closed,
}

/// <summary>How a state of the mark looks: its gradient left to right and its eyes, or no gradient for the one-color glyph, whose eyes are open.</summary>
internal sealed record MarkLook(IReadOnlyList<(float At, Rgb Color)>? Stops, MarkEyes Eyes)
{
    public static readonly Rgb EyeInk = Rgb.Parse("#15151A");

    private static readonly Rgb Cool = Rgb.Parse("#3D5BFF");
    private static readonly Rgb Violet = Rgb.Parse("#8A4DFF");
    private static readonly Rgb Amber = Rgb.Parse("#FF9F1A");
    private static readonly Rgb Orange = Rgb.Parse("#FF7A1A");
    private static readonly Rgb Red = Rgb.Parse("#FF3B2F");

    public static MarkLook Of(MarkState state) => state switch
    {
        MarkState.Calm => new([(0, Cool), (1, Violet)], MarkEyes.Closed),
        MarkState.Warm => new([(0, Amber), (1, Orange)], MarkEyes.Open),
        MarkState.Hot => new([(0, Orange), (1, Red)], MarkEyes.Wide),
        MarkState.App => new([(0, Cool), (0.6f, Violet), (1, Orange)], MarkEyes.Open),
        MarkState.Stopped or MarkState.HighContrast or _ => new(null, MarkEyes.Open),
    };

    /// <summary>The gradient's color at <paramref name="at"/>, from 0 at its left to 1 at its right, held at its ends beyond them.</summary>
    public Rgb At(double at)
    {
        var stops = Stops!;
        var t = Math.Clamp(at, 0, 1);
        var next = Math.Max(1, stops.ToList().FindIndex(stop => stop.At >= t));
        var (from, to) = (stops[next - 1], stops[next]);
        var part = (t - from.At) / (to.At - from.At);
        return new Rgb(Mix(from.Color.R, to.Color.R, part), Mix(from.Color.G, to.Color.G, part), Mix(from.Color.B, to.Color.B, part));
    }

    private static byte Mix(byte from, byte to, double part) => (byte)Math.Round(from + ((to - from) * part));
}

/// <summary>
/// thermi's mark, a capsule creature peeking over a line, the taskbar's edge, at any size: hand-placed pixels at 16 and
/// 24 px, the vector above. Each state has its gradient and eyes; stopped sensors and high contrast draw the one-color
/// glyph in <c>ink</c>, the body with its eyes cut out. The line is always in <c>ink</c>, the taskbar's text color.
/// </summary>
internal static class MarkPainter
{
    // The vector's own units: a 48 square, the body 36 by 23 from (6, 10), the line under it shorter and thinner.
    private const float Units = 48;
    private const float Stroke = 2.4f;

    public static void Draw(Graphics g, MarkState state, Rgb ink, RectangleF box)
    {
        var look = MarkLook.Of(state);
        if (MarkPixels.For((int)box.Width) is { } pixels)
        {
            pixels.Draw(g, look, ink, box);
            return;
        }

        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(box.Left, box.Top);
        g.ScaleTransform(box.Width / Units, box.Height / Units);
        Vector(g, look, ink);
        g.Restore(saved);
    }

    private static void Vector(Graphics g, MarkLook look, Rgb ink)
    {
        using var body = Body();
        if (look.Stops is { } stops)
        {
            using var fill = new LinearGradientBrush(new RectangleF(6, 10, 36, 23), PainterShapes.Drawn(stops[0].Color), PainterShapes.Drawn(stops[^1].Color), LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend { Colors = [.. stops.Select(stop => PainterShapes.Drawn(stop.Color))], Positions = [.. stops.Select(stop => stop.At)] },
                // Flipped rather than repeated, so an edge pixel past the gradient's box takes its own end's color.
                WrapMode = WrapMode.TileFlipX,
            };
            g.FillPath(fill, body);
            Eyes(g, look.Eyes, MarkLook.EyeInk);
        }
        else
        {
            // The eyes are holes: the body and the eyes in one path, filled alternately.
            foreach (var x in new[] { 25f, 33f })
            {
                using var eye = Eye(x, MarkEyes.Open);
                body.AddPath(eye, connect: false);
            }

            using var glyph = new SolidBrush(PainterShapes.Drawn(ink));
            g.FillPath(glyph, body);
        }

        using var line = new Pen(PainterShapes.Drawn(ink), Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, 10, 37.5f, 38, 37.5f);
    }

    // A flat bottom on the line's side, sides that rise straight and round shoulders of radius 12.
    private static GraphicsPath Body()
    {
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddLine(6, 33, 6, 22);
        path.AddArc(6, 10, 24, 24, 180, 90);
        path.AddArc(18, 10, 24, 24, 270, 90);
        path.AddLine(42, 22, 42, 33);
        path.CloseFigure();
        return path;
    }

    private static void Eyes(Graphics g, MarkEyes eyes, Rgb color)
    {
        foreach (var x in new[] { 25f, 33f })
        {
            if (eyes == MarkEyes.Closed)
            {
                // A smile turned down: the quadratic curve from (x - 3, 21) through (x, 24) to (x + 3, 21), as a cubic.
                using var lid = new Pen(PainterShapes.Drawn(color), Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawBezier(lid, x - 3, 21, x - 1, 23, x + 1, 23, x + 3, 21);
                continue;
            }

            using var eye = Eye(x, eyes);
            using var brush = new SolidBrush(PainterShapes.Drawn(color));
            g.FillPath(brush, eye);
        }
    }

    // An upright oval leaning 8° right, larger and a little higher when wide.
    private static GraphicsPath Eye(float x, MarkEyes eyes)
    {
        var (y, rx, ry) = eyes == MarkEyes.Wide ? (18.5f, 3.1f, 4.4f) : (19f, 2.6f, 3.7f);
        var path = new GraphicsPath();
        path.AddEllipse(x - rx, y - ry, 2 * rx, 2 * ry);
        using var lean = new Matrix();
        lean.RotateAt(8, new PointF(x, 20));
        path.Transform(lean);
        return path;
    }
}
