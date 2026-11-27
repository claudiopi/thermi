using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.Core.Painting;

/// <summary>Where an item was drawn, half the gap to its neighbors included, and where its value was, in the picture's pixels.</summary>
public sealed record PaintedItem(string Id, PillBox Item, PillBox Value)
{
    /// <summary>The pill the item was drawn in, which a drag of it carries off.</summary>
    public PillBox Pill { get; init; }

    /// <summary>Where the label its pill shows was drawn, from the item's start to its graph or value; empty for an item that drew none.</summary>
    public PillBox Label { get; init; }

    /// <summary>Where the item drew past its pill's label: its value and what it draws before it, which it lifts alone.</summary>
    public PillBox Alone { get; init; }
}

/// <summary>
/// Where a press landed in an overlay's pixels: the item under it, the pill it is drawn in, its own place, the part of it
/// past its pill's label, and whether it is on that label.
/// </summary>
public readonly record struct PressedOn(string? Item, PillBox Pill, PillBox Place, PillBox Alone, bool OnLabel);

/// <summary>What a point of an overlay's last paint is on, as the places its painter drew say, the overlay's edge being none.</summary>
public static class PaintedPlaces
{
    /// <summary>The item drawn at a point, or null on the overlay's edge.</summary>
    public static PaintedItem? At(this IReadOnlyList<PaintedItem> places, double x, double y) =>
        places.FirstOrDefault(place => place.Id.Length > 0 && place.Item.Contains(x, y));

    /// <summary>What a press at a point is on: an item, on its pill's label or not, or nothing on the overlay's edge.</summary>
    public static PressedOn Pressed(this IReadOnlyList<PaintedItem> places, double x, double y) =>
        places.At(x, y) is { } place ? new PressedOn(place.Id, place.Pill, place.Item, place.Alone, place.Label.Contains(x, y)) : default;

    /// <summary>
    /// Where a drop at a point joins the overlay <paramref name="onto"/>: before the first item whose pill's middle lies past
    /// it along the overlay, null past the last; or, for the item <paramref name="alone"/> carried over its own pill, before
    /// the first item of that pill whose middle lies past it along the pill's row, null past the pill's last.
    /// </summary>
    public static DropTarget JoinAt(this IReadOnlyList<PaintedItem> places, int onto, double x, double y, string? alone, bool vertical)
    {
        var items = places.Where(place => place.Id.Length > 0).ToList();
        if (alone is not null && items.Find(place => place.Id == alone)?.Pill is { } own && own.Contains(x, y))
        {
            return new DropTarget(onto, items.Find(place => place.Pill == own && place.Item.Left + (place.Item.Width / 2) > x)?.Id, InPill: true);
        }

        var after = items.Find(place => vertical ? place.Pill.Top + (place.Pill.Height / 2) > y : place.Pill.Left + (place.Pill.Width / 2) > x);
        return new DropTarget(onto, after?.Id);
    }
}
