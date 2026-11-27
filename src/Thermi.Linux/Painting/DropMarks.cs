using Thermi.Core.Look;
using Thermi.Core.Painting;

namespace Thermi.Linux.Painting;

/// <summary>
/// What a drag marks on an overlay's picture, as Windows marks it: the gap a lifted pill leaves, so the pill shows once,
/// where it is carried, and, on the overlay a drop would join, a ring around it and a bar where the pill or overlay goes,
/// in the accent color. Sizes are in pixels at 100 %, times the scale.
/// </summary>
internal static class DropMarks
{
    private const double Ring = 2;
    private const double Bar = 4;

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
        var line = Math.Max(1, scale);
        var slot = new PillBox(pill.Left + line, pill.Top + line, pill.Width - (2 * line), pill.Height - (2 * line));
        PainterShapes.Rounded(cr, slot, slot.Height / 2);
        PainterShapes.Source(cr, Gray, GapTint);
        cr.FillPreserve();
        PainterShapes.Source(cr, Gray, GapLine);
        cr.LineWidth = line;
        cr.SetDash([3 * line, 2 * line], 0);
        cr.Stroke();
    }

    /// <summary>
    /// Rings the overlay, and draws a bar before the pill of the item <paramref name="at"/> names, or past the last pill
    /// for none, across the overlay: across its row, or its column when <paramref name="vertical"/>. For an item carried
    /// within its own pill, the bar stands in that pill, before the item named or past the pill's last.
    /// </summary>
    public static void Join(Picture picture, IReadOnlyList<PaintedItem> places, (string? Before, string? Within) at, bool vertical, Rgb accent, double scale)
    {
        using var cr = new Cairo.Context(picture.Surface);
        var ring = Ring * scale;
        var box = new PillBox(ring / 2, ring / 2, picture.Width - ring, picture.Height - ring);
        PainterShapes.Rounded(cr, box, Math.Min(box.Width, box.Height) / 2);
        PainterShapes.Source(cr, accent);
        cr.LineWidth = ring;
        cr.Stroke();

        var pills = places.Where(place => place.Id.Length > 0).ToList();
        var thick = Bar * scale;
        var bar = pills.FirstOrDefault(place => place.Id == at.Within)?.Pill is { } within
            ? Between([.. pills.Where(place => place.Pill == within)], at.Before, within, thick)
            : Before(pills, at.Before, (picture.Width, picture.Height), vertical, thick, ring);
        PainterShapes.Rounded(cr, bar, thick / 2);
        cr.Fill();
    }

    private static PillBox Before(List<PaintedItem> pills, string? before, (int Width, int Height) size, bool vertical, double thick, double ring)
    {
        var at = pills.FirstOrDefault(place => place.Id == before)?.Pill;
        var last = pills.Count == 0 ? default : pills[^1].Pill;
        var edge = at is { } pill ? (vertical ? pill.Top : pill.Left) - thick : vertical ? last.Bottom : last.Right;
        return vertical
            ? new PillBox(ring, Edge(edge, size.Height, thick, ring), size.Width - (2 * ring), thick)
            : new PillBox(Edge(edge, size.Width, thick, ring), ring, thick, size.Height - (2 * ring));
    }

    private static PillBox Between(List<PaintedItem> items, string? before, PillBox pill, double thick)
    {
        var x = items.FirstOrDefault(place => place.Id == before)?.Item.Left ?? items[^1].Item.Right;
        return new PillBox(Math.Clamp(x - (thick / 2), pill.Left, pill.Right - thick), pill.Top, thick, pill.Height);
    }

    // Kept inside the ring, at either end.
    private static double Edge(double at, int length, double thick, double ring) => Math.Clamp(at, ring, Math.Max(ring, length - ring - thick));
}
