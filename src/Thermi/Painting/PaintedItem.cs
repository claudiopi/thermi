using System.Drawing;

namespace Thermi.Painting;

/// <summary>Where an item was drawn, half the gap to its neighbors included, and where its value was, in the bitmap's pixels.</summary>
internal sealed record PaintedItem(string Id, RectangleF Item, RectangleF Value)
{
    /// <summary>The pill the item was drawn in, which a drag of it carries off.</summary>
    public RectangleF Pill { get; init; }
}
