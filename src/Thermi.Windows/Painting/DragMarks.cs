using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Painting;

namespace Thermi.Windows.Painting;

/// <summary>
/// What a drag marks on an overlay's picture, where Core's <see cref="DropMarkShapes"/> puts it: the gap a lifted pill
/// leaves, so the pill shows once, where it is carried, and, on the overlay a drop would join, a ring and a bar, in the
/// accent color.
/// </summary>
internal static class DragMarks
{
    // The gap shows as an empty slot, faint on any background: a dashed outline over a light tint.
    private const int GapTint = 40;
    private const int GapLine = 140;

    /// <summary>Clears the pill at <paramref name="pill"/> and leaves an empty slot of its size.</summary>
    public static void Gap(Bitmap bitmap, RectangleF pill, float scale)
    {
        if (pill.IsEmpty)
        {
            return;
        }

        using var g = Graphics.FromImage(bitmap);
        g.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(Color.Transparent);
        g.FillRectangle(clear, RectangleF.Inflate(pill, 1, 1));
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var line = (float)DropMarkShapes.SlotLine(scale);
        var slot = DropMarkShapes.Slot(pill.Box(), scale).Rect();
        using var shape = PainterShapes.Rounded(slot, slot.Height / 2);
        using var tint = new SolidBrush(Color.FromArgb(GapTint, Color.Gray));
        using var pen = new Pen(Color.FromArgb(GapLine, Color.Gray), line) { DashStyle = DashStyle.Dash };
        g.FillPath(tint, shape);
        g.DrawPath(pen, shape);
    }

    /// <summary>Rings the overlay, and draws the bar where a drop of the item <paramref name="at"/> names goes.</summary>
    public static void Join(Bitmap bitmap, IReadOnlyList<PaintedItem> places, (string? Before, string? Within) at, bool vertical, Rgb accent, float scale)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = PainterShapes.Drawn(accent);
        var box = DropMarkShapes.RingBox((bitmap.Width, bitmap.Height), scale).Rect();
        using (var outline = PainterShapes.Rounded(box, Math.Min(box.Width, box.Height) / 2))
        using (var pen = new Pen(color, (float)DropMarkShapes.RingLine(scale)))
        {
            g.DrawPath(pen, outline);
        }

        var bar = DropMarkShapes.Bar(places, at, vertical, (bitmap.Width, bitmap.Height), scale).Rect();
        using var shape = PainterShapes.Rounded(bar, (float)DropMarkShapes.BarThickness(scale) / 2);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, shape);
    }
}
