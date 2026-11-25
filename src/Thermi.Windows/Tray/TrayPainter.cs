using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Thermi.Core.Tray;
using Thermi.Windows.Painting;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tray;

/// <summary>
/// Draws what Core's <see cref="TrayScene"/> says a tray icon shows, square at the notification area's icon size: an
/// item's characters as large as the icon lets them be, or thermi's mark.
/// </summary>
internal static class TrayPainter
{
    // The shapes are made at this size and then scaled, which keeps their proportions exact at any size.
    private const float Reference = 100;

    // Condensed, so four characters stay tall enough to read in a 16 px icon, and semibold, whose strokes cover whole pixels
    // there. Windows ships it since Windows 10.
    private static readonly string Face = PainterFonts.Family(["Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Bahnschrift", "Segoe UI"]);

    /// <summary>A new icon handle, which the caller destroys.</summary>
    public static HICON HandleOf(TrayIcon icon, int size)
    {
        using var bitmap = Paint(icon, size);
        return (HICON)bitmap.GetHicon();
    }

    public static Bitmap Paint(TrayIcon icon, int size)
    {
        Bitmap? bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var brush = new SolidBrush(PainterShapes.Drawn(icon.Color));
            if (icon.Mark is { } mark)
            {
                MarkPainter.Draw(g, mark, icon.Color, new RectangleF(0, 0, size, size));
            }
            else if (icon.Text is { } text)
            {
                Characters(g, text, brush, size);
            }

            var painted = bitmap;
            bitmap = null;
            return painted;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    // The characters are as large as the icon lets them be, measured by their ink and the digits' height together, so a
    // dash keeps a dash's size. That box is a whole number of pixels high with its top on a pixel, so the tops and feet of
    // the digits stay sharp.
    private static void Characters(Graphics g, string text, Brush brush, int size)
    {
        using var family = new FontFamily(Face);
        using var path = Outline(text, family);
        using var digit = Outline("0", family);
        var (ink, digits) = (path.GetBounds(), digit.GetBounds());
        if (ink.Width <= 0)
        {
            return;
        }

        var box = RectangleF.FromLTRB(ink.Left, Math.Min(ink.Top, digits.Top), ink.Right, Math.Max(ink.Bottom, digits.Bottom));
        var scale = Math.Min(size / box.Width, size / box.Height);
        scale = Math.Max(1, MathF.Floor(box.Height * scale)) / box.Height;
        using var matrix = new Matrix();
        matrix.Translate(MathF.Round((size - (box.Width * scale)) / 2), MathF.Round((size - (box.Height * scale)) / 2));
        matrix.Scale(scale, scale);
        matrix.Translate(-box.X, -box.Y);
        path.Transform(matrix);
        g.FillPath(brush, path);
    }

    private static GraphicsPath Outline(string text, FontFamily family)
    {
        var path = new GraphicsPath();
        path.AddString(text, family, (int)FontStyle.Regular, Reference, PointF.Empty, PainterFonts.Typographic);
        return path;
    }
}
