using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Thermi.Core.Look;

namespace Thermi.Windows.Painting;

/// <summary>
/// Outlined text, each of its looks stroked once and then copied: GDI+ strokes a text's outline slower than anything else
/// an overlay draws, and an overlay draws the same labels, and many of the same values, every second. A look left undrawn
/// for a few paints is let go.
/// </summary>
internal sealed class OutlinedTexts : IDisposable
{
    // Past these a look goes, so a huge overlay's values never hold much memory.
    private const int Paints = 8;
    private const int MostPixels = 1 << 20;

    // A text's place within its pixel, in these steps, so a copy lands within a 32nd of a pixel of where a stroke would.
    private const int Steps = 16;

    private readonly Dictionary<Look, Drawn> _drawn = [];
    private int _paint;
    private int _pixels;

    /// <summary>Draws <paramref name="text"/> at <paramref name="origin"/>, filled in its color over its outline.</summary>
    public void Draw(Graphics g, string text, Font font, Rgb color, Rgb outline, float width, PointF origin)
    {
        var (x, y) = ((int)MathF.Round(origin.X * Steps), (int)MathF.Round(origin.Y * Steps));
        var (left, top) = ((int)MathF.Floor((float)x / Steps), (int)MathF.Floor((float)y / Steps));
        var look = new Look(text, font.FontFamily.Name, font.Size, font.Style, color, outline, width, x - (left * Steps), y - (top * Steps));
        if (!_drawn.TryGetValue(look, out var drawn))
        {
            drawn = Stroked(look, font);
            if (_pixels + drawn.Pixels > MostPixels)
            {
                Copy(g, drawn, left, top);
                drawn.Picture.Dispose();
                return;
            }

            _pixels += drawn.Pixels;
        }

        _drawn[look] = drawn with { Used = _paint };
        Copy(g, drawn, left, top);
    }

    /// <summary>A paint ended: the looks none of the last few drew go.</summary>
    public void Painted()
    {
        _paint++;
        foreach (var (look, drawn) in _drawn.Where(each => each.Value.Used < _paint - Paints).ToList())
        {
            _drawn.Remove(look);
            _pixels -= drawn.Pixels;
            drawn.Picture.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var drawn in _drawn.Values)
        {
            drawn.Picture.Dispose();
        }

        _drawn.Clear();
    }

    // Pixel for pixel: an interpolated copy would blur it.
    private static void Copy(Graphics g, Drawn drawn, int left, int top)
    {
        var interpolation = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        var (width, height) = (drawn.Picture.Width, drawn.Picture.Height);
        g.DrawImage(drawn.Picture, new Rectangle(left + drawn.Offset.X, top + drawn.Offset.Y, width, height), 0, 0, width, height, GraphicsUnit.Pixel);
        g.InterpolationMode = interpolation;
    }

    // The outline is the text's own shape stroked and then filled, so the two always line up.
    private static Drawn Stroked(Look look, Font font)
    {
        using var path = new GraphicsPath();
        using var pen = new Pen(PainterShapes.Drawn(look.Outline), look.Width) { LineJoin = LineJoin.Round };
        var origin = new PointF((float)look.X / Steps, (float)look.Y / Steps);
        path.AddString(look.Text, font.FontFamily, (int)font.Style, font.Size, origin, PainterFonts.Typographic);
        var bounds = path.GetBounds(null, pen);
        var offset = new Point((int)MathF.Floor(bounds.Left) - 1, (int)MathF.Floor(bounds.Top) - 1);
        var size = new Size(Math.Max(1, (int)MathF.Ceiling(bounds.Right) + 1 - offset.X), Math.Max(1, (int)MathF.Ceiling(bounds.Bottom) + 1 - offset.Y));
#pragma warning disable CA2000 // Disposed on failure here; drawn, the caller owns it and disposes it once it is let go.
        var picture = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
#pragma warning restore CA2000
        try
        {
            using var g = Graphics.FromImage(picture);
            using var brush = new SolidBrush(PainterShapes.Drawn(look.Color));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(-offset.X, -offset.Y);
            g.DrawPath(pen, path);
            g.FillPath(brush, path);
            return new Drawn(picture, offset, size.Width * size.Height, 0);
        }
        catch
        {
            picture.Dispose();
            throw;
        }
    }

    private readonly record struct Look(string Text, string Family, float Size, FontStyle Style, Rgb Color, Rgb Outline, float Width, int X, int Y);

    private readonly record struct Drawn(Bitmap Picture, Point Offset, int Pixels, int Used);
}
