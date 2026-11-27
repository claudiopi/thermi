using Thermi.Core.Look;

namespace Thermi.Core.Painting;

/// <summary>
/// Where a drag's marks go on an overlay's picture, which each OS's painter draws its own way: the slot a lifted pill
/// leaves, and, on the overlay a drop would join, a ring around it and a bar where the pill or overlay goes. Sizes are in
/// pixels at 100 %, times the scale.
/// </summary>
public static class DropMarkShapes
{
    private const double Ring = 2;
    private const double BarWidth = 4;

    public static double RingLine(double scale) => Ring * scale;

    public static double SlotLine(double scale) => Math.Max(1, scale);

    public static PillBox Slot(PillBox pill, double scale)
    {
        var line = SlotLine(scale);
        return new PillBox(pill.Left + line, pill.Top + line, pill.Width - (2 * line), pill.Height - (2 * line));
    }

    /// <summary>The ring around a picture <paramref name="size"/> big, its line's middle on the box.</summary>
    public static PillBox RingBox((double Width, double Height) size, double scale)
    {
        var ring = RingLine(scale);
        return new PillBox(ring / 2, ring / 2, size.Width - ring, size.Height - ring);
    }

    /// <summary>
    /// The bar before the pill of the item <paramref name="at"/> names, or past the last pill for none, across the
    /// overlay: across its row, or its column when <paramref name="vertical"/>. For an item carried within its own pill,
    /// the bar stands in that pill, before the item named or past the pill's last.
    /// </summary>
    public static PillBox Bar(
        IReadOnlyList<PaintedItem> places, (string? Before, string? Within) at, bool vertical, (double Width, double Height) size, double scale)
    {
        var pills = places.Where(place => place.Id.Length > 0).ToList();
        var thick = BarThickness(scale);
        return pills.FirstOrDefault(place => place.Id == at.Within)?.Pill is { } within
            ? Between([.. pills.Where(place => place.Pill == within)], at.Before, within, thick)
            : Before(pills, at.Before, size, vertical, thick, RingLine(scale));
    }

    public static double BarThickness(double scale) => BarWidth * scale;

    private static PillBox Before(List<PaintedItem> pills, string? before, (double Width, double Height) size, bool vertical, double thick, double ring)
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

    private static double Edge(double at, double length, double thick, double ring) => Math.Clamp(at, ring, Math.Max(ring, length - ring - thick));
}
