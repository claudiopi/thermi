using Thermi.Core.Look;
using Thermi.Core.Painting;

namespace Thermi.Linux.Painting;

/// <summary>
/// What a drag marks on an overlay's picture, where Core's <see cref="DropMarkShapes"/> puts it, as Windows marks it: the
/// gap a lifted pill leaves, so the pill shows once, where it is carried, and, on the overlay a drop would join, a ring
/// and a bar, in the accent color.
/// </summary>
internal static class DropMarks
{
    // The gap shows as an empty slot, faint on any background: a dashed outline over a light tint.
    private const double GapTint = 40 / 255.0;
    private const double GapLine = 140 / 255.0;
    private static readonly Rgb Gray = new(128, 128, 128);

    /// <summary>Clears the pill at <paramref name="pill"/> and leaves an empty slot of its size.</summary>
    public static void Gap(Picture picture, PillBox pill, double scale)
    {
        if (pill.Width <= 0 || pill.Height <= 0)
        {
            return;
        }

        using var cr = new Cairo.Context(picture.Surface);
        cr.Operator = Cairo.Operator.Clear;
        cr.Rectangle(pill.Left - 1, pill.Top - 1, pill.Width + 2, pill.Height + 2);
        cr.Fill();
        cr.Operator = Cairo.Operator.Over;
        var line = DropMarkShapes.SlotLine(scale);
        var slot = DropMarkShapes.Slot(pill, scale);
        PainterShapes.Rounded(cr, slot, slot.Height / 2);
        PainterShapes.Source(cr, Gray, GapTint);
        cr.FillPreserve();
        PainterShapes.Source(cr, Gray, GapLine);
        cr.LineWidth = line;
        cr.SetDash([3 * line, 2 * line], 0);
        cr.Stroke();
    }

    public static void RingAndBar(Picture picture, IReadOnlyList<PaintedItem> places, (string? Before, string? Within) at, bool vertical, Rgb accent, double scale)
    {
        using var cr = new Cairo.Context(picture.Surface);
        var box = DropMarkShapes.RingBox((picture.Width, picture.Height), scale);
        PainterShapes.Rounded(cr, box, Math.Min(box.Width, box.Height) / 2);
        PainterShapes.Source(cr, accent);
        cr.LineWidth = DropMarkShapes.RingLine(scale);
        cr.Stroke();
        PainterShapes.Rounded(cr, DropMarkShapes.Bar(places, at, vertical, (picture.Width, picture.Height), scale), DropMarkShapes.BarThickness(scale) / 2);
        cr.Fill();
    }
}
