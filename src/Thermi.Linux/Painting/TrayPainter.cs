using Thermi.Core.Look;
using Thermi.Core.Tray;

namespace Thermi.Linux.Painting;

/// <summary>
/// Draws what Core's <see cref="TrayScene"/> says a tray icon shows, as Windows' painter does: square, an item's characters
/// as large as the icon lets them be, or thermi's mark.
/// </summary>
internal static class TrayPainter
{
    /// <summary>
    /// A tray icon's side in logical pixels: the freedesktop panels' icon size, which KDE Plasma's tray and the GNOME Shell's
    /// top bar both fit.
    /// </summary>
    public const int Size = 22;

    // The characters are laid out at this size and then scaled, which keeps their proportions exact at any size.
    private const double Reference = 100;

    // Condensed and semibold, as Windows' Bahnschrift, so four characters stay tall enough to read; each falls back to the
    // condensed faces Linux desktops ship.
    private const string Faces = "Bahnschrift, Roboto Condensed, Ubuntu Condensed, DejaVu Sans Condensed, Noto Sans, DejaVu Sans, sans-serif";

    /// <summary>The icon's side in pixels at a monitor's scale.</summary>
    public static int PixelsAt(double scale) => Math.Max(1, (int)Math.Round(Size * scale));

    public static Picture Paint(TrayIcon icon, int size)
    {
        Picture? picture = new Picture(size, size);
        try
        {
            using (var cr = new Cairo.Context(picture.Surface))
            {
                if (icon.Mark is { } mark)
                {
                    MarkPainter.Draw(cr, mark, icon.Color, size);
                }
                else if (icon.Text is { } text)
                {
                    Characters(cr, text, icon.Color, size);
                }
            }

            var painted = picture;
            picture = null;
            return painted;
        }
        finally
        {
            picture?.Dispose();
        }
    }

    // As large as the icon lets them be, measured by their ink and the digits' height together, so a dash keeps a dash's
    // size; that box is a whole number of pixels high with its top on a pixel, so the digits' tops and feet stay sharp.
    private static void Characters(Cairo.Context cr, string text, Rgb color, int size)
    {
        using var layout = PangoCairo.Functions.CreateLayout(cr);
        var font = Pango.FontDescription.FromString(Faces);
        font.SetWeight(Pango.Weight.Semibold);
        font.SetStretch(Pango.Stretch.Condensed);
        font.SetAbsoluteSize(Reference * Pango.Constants.SCALE);
        layout.SetFontDescription(font);
        var (left, inkTop, right, inkBottom) = Extents(cr, layout, text);
        var (_, digitTop, _, digitBottom) = Extents(cr, layout, "0");
        if (right <= left)
        {
            return;
        }

        var (top, bottom) = (Math.Min(inkTop, digitTop), Math.Max(inkBottom, digitBottom));
        var (width, height) = (right - left, bottom - top);
        var scale = Math.Min(size / width, size / height);
        scale = Math.Max(1, Math.Floor(height * scale)) / height;
        cr.Translate(Math.Round((size - (width * scale)) / 2), Math.Round((size - (height * scale)) / 2));
        cr.Scale(scale, scale);
        cr.Translate(-left, -top);
        layout.SetText(text, -1);
        cr.MoveTo(0, 0);
        PangoCairo.Functions.LayoutPath(cr, layout);
        PainterShapes.Source(cr, color);
        cr.Fill();
    }

    private static (double Left, double Top, double Right, double Bottom) Extents(Cairo.Context cr, Pango.Layout layout, string text)
    {
        layout.SetText(text, -1);
        cr.NewPath();
        cr.MoveTo(0, 0);
        PangoCairo.Functions.LayoutPath(cr, layout);
        cr.FillExtents(out var left, out var top, out var right, out var bottom);
        cr.NewPath();
        return (left, top, right, bottom);
    }
}
