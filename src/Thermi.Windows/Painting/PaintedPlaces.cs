using System.Drawing;
using Thermi.Core.Placement;

namespace Thermi.Windows.Painting;

/// <summary>
/// Where a press landed in an overlay's pixels: the item under it, the pill it is drawn in, its own place, and whether it
/// is on the label its pill shows.
/// </summary>
internal readonly record struct PressedOn(string? Item, RectangleF Pill, RectangleF Place, bool OnLabel);

/// <summary>What a point of an overlay's last paint is on, as the places its painter drew say, the overlay's edge being none.</summary>
internal static class PaintedPlaces
{
    /// <summary>The item drawn at a point, or null on the overlay's edge.</summary>
    public static PaintedItem? At(this IReadOnlyList<PaintedItem> places, PointF at) =>
        places.FirstOrDefault(place => place.Id.Length > 0 && place.Item.Contains(at));

    /// <summary>What a press at a point is on: an item, on its pill's label or not, or nothing on the overlay's edge.</summary>
    public static PressedOn Pressed(this IReadOnlyList<PaintedItem> places, PointF at) =>
        places.At(at) is { } place ? new PressedOn(place.Id, place.Pill, place.Item, place.Label.Contains(at)) : default;

    /// <summary>
    /// Where a drop at a point joins the overlay <paramref name="onto"/>: before the first item whose pill's middle lies past
    /// it along the overlay, null past the last; or, for the item <paramref name="alone"/> carried over its own pill, before
    /// the first item of that pill whose middle lies past it along the pill's row, null past the pill's last.
    /// </summary>
    public static DropTarget JoinAt(this IReadOnlyList<PaintedItem> places, int onto, PointF at, string? alone, bool vertical)
    {
        var items = places.Where(place => place.Id.Length > 0).ToList();
        if (alone is not null && items.Find(place => place.Id == alone)?.Pill is { } own && own.Contains(at))
        {
            return new DropTarget(onto, items.Find(place => place.Pill == own && place.Item.Left + (place.Item.Width / 2) > at.X)?.Id, InPill: true);
        }

        var after = items.Find(place => vertical ? place.Pill.Top + (place.Pill.Height / 2) > at.Y : place.Pill.Left + (place.Pill.Width / 2) > at.X);
        return new DropTarget(onto, after?.Id);
    }
}
