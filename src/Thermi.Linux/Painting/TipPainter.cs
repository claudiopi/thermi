using Thermi.Core.Look;

namespace Thermi.Linux.Painting;

/// <summary>
/// A value's tooltip as a picture, for desktops that show none an overlay can use: its lines on a rounded box, padded, dark
/// or light as the desktop's apps are, or in high contrast's colors, as Windows' tooltip control draws it there.
/// </summary>
internal static class TipPainter
{
    // In pixels at 100 %, as the desktop's own tooltips are.
    private const double TextSize = 13;
    private const double Padding = 6;
    private const double Radius = 6;

    private static readonly (Rgb Back, Rgb Text, double Border) Dark = (new Rgb(0x24, 0x24, 0x24), new Rgb(0xff, 0xff, 0xff), 0.12);
    private static readonly (Rgb Back, Rgb Text, double Border) Light = (new Rgb(0xfa, 0xfa, 0xfa), new Rgb(0x1e, 0x1e, 0x1e), 0.18);

    public static Picture Paint(string text, double scale, bool dark, HighContrastColors? contrast)
    {
        var (back, color, border) = contrast is { } colors ? (colors.Window, colors.WindowText, 1.0) : dark ? Dark : Light;
        using var measured = new Cairo.ImageSurface(Cairo.Format.Argb32, 1, 1);
        using var measure = new Cairo.Context(measured);
        using var layout = Layout(measure, text, scale);
        layout.GetExtents(out _, out var logical);
        var pad = Padding * scale;
        var (width, height) = (logical.Width / (double)Pango.Constants.SCALE, logical.Height / (double)Pango.Constants.SCALE);
        var picture = new Picture((int)Math.Ceiling(width + (2 * pad)), (int)Math.Ceiling(height + (2 * pad)));
        try
        {
            using var cr = new Cairo.Context(picture.Surface);
            var edge = Math.Max(1, Math.Round(scale));
            var box = new PillBox(edge / 2, edge / 2, picture.Width - edge, picture.Height - edge);
            PainterShapes.Rounded(cr, box, Radius * scale);
            PainterShapes.Source(cr, back);
            cr.FillPreserve();
            PainterShapes.Source(cr, color, border);
            cr.LineWidth = edge;
            cr.Stroke();
            using var drawn = Layout(cr, text, scale);
            PainterShapes.Source(cr, color);
            cr.MoveTo(pad, pad);
            PangoCairo.Functions.ShowLayout(cr, drawn);
            return picture;
        }
        catch
        {
            picture.Dispose();
            throw;
        }
    }

    private static Pango.Layout Layout(Cairo.Context cr, string text, double scale)
    {
        var layout = PangoCairo.Functions.CreateLayout(cr);
        var font = Pango.FontDescription.FromString("Cantarell, Inter, Noto Sans, DejaVu Sans, sans-serif");
        font.SetAbsoluteSize(TextSize * scale * Pango.Constants.SCALE);
        layout.SetFontDescription(font);
        layout.SetText(text, -1);
        return layout;
    }
}
