using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;

namespace Thermi.Painting;

/// <summary>
/// What a drag marks on an overlay's picture: the gap a lifted pill leaves, so the pill shows once, where it is carried,
/// and, on the overlay a drop would join, a ring around it and a bar where the pill or overlay goes, in the accent color.
/// Sizes are in pixels at 100 %, times the scale.
/// </summary>
internal static class DragMarks
{
    private const float Ring = 2;
    private const float Bar = 4;

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
        var line = Math.Max(1, scale);
        var slot = RectangleF.Inflate(pill, -line, -line);
        using var shape = PainterShapes.Rounded(slot, slot.Height / 2);
        using var tint = new SolidBrush(Color.FromArgb(GapTint, Color.Gray));
        using var pen = new Pen(Color.FromArgb(GapLine, Color.Gray), line) { DashStyle = DashStyle.Dash };
        g.FillPath(tint, shape);
        g.DrawPath(pen, shape);
    }

    /// <summary>
    /// Rings the overlay, and draws a bar before the pill of the item <paramref name="before"/>, or past the last pill
    /// for none, across the overlay: across its row, or its column when <paramref name="vertical"/>.
    /// </summary>
    public static void Join(Bitmap bitmap, IReadOnlyList<PaintedItem> places, string? before, bool vertical, Rgb accent, float scale)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = PainterShapes.Drawn(accent);
        var ring = Ring * scale;
        var box = new RectangleF(ring / 2, ring / 2, bitmap.Width - ring, bitmap.Height - ring);
        using (var outline = PainterShapes.Rounded(box, Math.Min(box.Width, box.Height) / 2))
        using (var pen = new Pen(color, ring))
        {
            g.DrawPath(pen, outline);
        }

        var pills = places.Where(place => place.Id.Length > 0).ToList();
        var at = pills.FirstOrDefault(place => place.Id == before)?.Pill;
        var last = pills.Count == 0 ? RectangleF.Empty : pills[^1].Pill;
        var thick = Bar * scale;
        var edge = at is { } pill ? (vertical ? pill.Top : pill.Left) - thick : vertical ? last.Bottom : last.Right;
        var bar = vertical
            ? new RectangleF(ring, Edge(edge, bitmap.Height, thick, ring), bitmap.Width - (2 * ring), thick)
            : new RectangleF(Edge(edge, bitmap.Width, thick, ring), ring, thick, bitmap.Height - (2 * ring));
        using var shape = PainterShapes.Rounded(bar, thick / 2);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, shape);
    }

    // Kept inside the ring, at either end.
    private static float Edge(float at, int length, float thick, float ring) => Math.Clamp(at, ring, Math.Max(ring, length - ring - thick));
}
