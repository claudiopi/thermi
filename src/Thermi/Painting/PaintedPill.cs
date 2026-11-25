using System.Drawing;
using Thermi.Core.Look;

namespace Thermi.Painting;

/// <summary>A part of a pill the painter draws items in: the pill itself, or a pill a single bar holds.</summary>
internal readonly record struct PillPart(RectangleF Box, float Start, int First, int Count);

/// <summary>A pill as the painter lays it out: its items and their widths, in its pixels.</summary>
internal sealed record PaintedPill(IReadOnlyList<OverlayItem> Items, List<float> ItemWidths, float Scale)
{
    /// <summary>In a single bar, how many of its items each pill inside it holds; null for a pill that holds none.</summary>
    public IReadOnlyList<int>? Runs { get; init; }

    /// <summary>Whether a single bar stands, its pills stacked from top to bottom, all as wide as the widest.</summary>
    public bool Stacked { get; init; }

    private float Padding => PainterShapes.OuterPadding * Scale;

    private float InnerGap => Painter.InnerPillGap * Scale;

    public float Width => Stacked
        ? (Padding * 2) + Spans.Max(span => SpanWidth(span.First, span.Count))
        : (Padding * 2) + Spans.Sum(span => SpanWidth(span.First, span.Count)) + (InnerGap * Math.Max(0, Spans.Count - 1));

    /// <summary>Its height, where a pill in a row is <paramref name="row"/> tall: a standing bar's holds each of its pills as a row does.</summary>
    public double HeightOf(double row) => Stacked ? (Spans.Count * (row - (2 * Padding))) + (2 * Padding) + (InnerGap * (Spans.Count - 1)) : row;

    private List<(int First, int Count)> Spans =>
        Runs is null ? [(0, Items.Count)] : [.. Runs.Select((count, at) => (Runs.Take(at).Sum(), count))];

    /// <summary>The pill at <paramref name="box"/> itself, or each pill a single bar holds inside its padding.</summary>
    public List<PillPart> PartsIn(RectangleF box)
    {
        if (Runs is null)
        {
            return [new PillPart(box, box.Left + Padding + (Painter.InnerPadding * Scale), 0, Items.Count)];
        }

        var spans = Spans;
        var parts = new List<PillPart>();
        var tall = Stacked ? (box.Height - (2 * Padding) - (InnerGap * (spans.Count - 1))) / spans.Count : box.Height - (2 * Padding);
        var (left, top) = (box.Left + Padding, box.Top + Padding);
        foreach (var (first, count) in spans)
        {
            var width = Stacked ? box.Width - (2 * Padding) : SpanWidth(first, count);
            parts.Add(new PillPart(new RectangleF(left, top, width, tall), left + (Painter.InnerPadding * Scale), first, count));
            (left, top) = Stacked ? (left, top + tall + InnerGap) : (left + width + InnerGap, top);
        }

        return parts;
    }

    private float SpanWidth(int first, int count) =>
        (Painter.InnerPadding * 2 * Scale) + ItemWidths.Skip(first).Take(count).Sum() + (Painter.ItemGap * Scale * Math.Max(0, count - 1));
}
