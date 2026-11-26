using System.Drawing;

namespace Thermi.Windows.Painting;

/// <summary>Where an item was drawn, half the gap to its neighbors included, and where its value was, in the bitmap's pixels.</summary>
internal sealed record PaintedItem(string Id, RectangleF Item, RectangleF Value)
{
    /// <summary>The pill the item was drawn in, which a drag of it carries off.</summary>
    public RectangleF Pill { get; init; }

    /// <summary>Where the label its pill shows was drawn, from the item's start to its graph or value; empty for an item that drew none.</summary>
    public RectangleF Label { get; init; }

    /// <summary>Where the item drew past its pill's label: its value and what it draws before it, which it lifts alone.</summary>
    public RectangleF Alone { get; init; }
}
