using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Windows.Painting;

namespace Thermi.Windows.Tray;

/// <summary>Draws Core's hand-placed mark (<see cref="Core.Painting.MarkPixels"/>) with GDI+, a whole pixel each.</summary>
internal static class MarkCells
{
    /// <summary>Paints the mark at <paramref name="box"/>'s top left corner, centered in its width.</summary>
    public static void Draw(Graphics g, Core.Painting.MarkPixels pixels, MarkLook look, Rgb ink, RectangleF box)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var size = pixels.Size;
        var offset = new Point((int)box.Left + (((int)box.Width - size) / 2), (int)box.Top + (((int)box.Height - size) / 2));
        using var inked = new SolidBrush(PainterShapes.Drawn(ink));
        foreach (var cell in pixels.Body(look))
        {
            using var brush = cell.Color is { } color ? new SolidBrush(PainterShapes.Drawn(color)) : null;
            g.FillRectangle(brush ?? inked, offset.X + cell.X, offset.Y + cell.Y, 1, 1);
        }

        var (y, from, to) = pixels.Line;
        g.FillRectangle(inked, offset.X + from, offset.Y + y, to - from + 1, 1);
        g.Restore(state);
    }
}
