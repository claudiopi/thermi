using Thermi.Core.Look;

namespace Thermi.Core.Painting;

/// <summary>A part of a pill a painter draws items in: the pill itself, or a pill a single bar holds.</summary>
public readonly record struct PillPart(PillBox Box, double Start, int First, int Count);

/// <summary>
/// A pill as a painter lays it out: its items and their widths, in its pixels, as the painter measured them. Every painter
/// places the pills a single bar holds, and the items in them, from here.
/// </summary>
public sealed record PaintedPill(IReadOnlyList<OverlayItem> Items, IReadOnlyList<double> ItemWidths, double Scale)
{
    /// <summary>In a single bar, how many of its items each pill inside it holds; null for a pill that holds none.</summary>
    public IReadOnlyList<int>? Runs { get; init; }

    /// <summary>Whether a single bar stands, its pills stacked from top to bottom, all as wide as the widest.</summary>
    public bool Stacked { get; init; }

    private double Padding => PaintSizes.OuterPadding * Scale;

    private double InnerGap => PaintSizes.InnerPillGap * Scale;

    public double Width => Stacked
        ? (Padding * 2) + Spans.Max(span => SpanWidth(span.First, span.Count))
        : (Padding * 2) + Spans.Sum(span => SpanWidth(span.First, span.Count)) + (InnerGap * Math.Max(0, Spans.Count - 1));

    /// <summary>Its height, where a pill in a row is <paramref name="row"/> tall: a standing bar's holds each of its pills as a row does.</summary>
    public double HeightOf(double row) => Stacked ? (Spans.Count * (row - (2 * Padding))) + (2 * Padding) + (InnerGap * (Spans.Count - 1)) : row;

    private List<(int First, int Count)> Spans =>
        Runs is null ? [(0, Items.Count)] : [.. Runs.Select((count, at) => (Runs.Take(at).Sum(), count))];

    /// <summary>The pill at <paramref name="box"/> itself, or each pill a single bar holds inside its padding.</summary>
    public IReadOnlyList<PillPart> PartsIn(PillBox box)
    {
        if (Runs is null)
        {
            return [new PillPart(box, box.Left + Padding + (PaintSizes.InnerPadding * Scale), 0, Items.Count)];
        }

        var spans = Spans;
        var parts = new List<PillPart>();
        var tall = Stacked ? (box.Height - (2 * Padding) - (InnerGap * (spans.Count - 1))) / spans.Count : box.Height - (2 * Padding);
        var (left, top) = (box.Left + Padding, box.Top + Padding);
        foreach (var (first, count) in spans)
        {
            var width = Stacked ? box.Width - (2 * Padding) : SpanWidth(first, count);
            parts.Add(new PillPart(new PillBox(left, top, width, tall), left + (PaintSizes.InnerPadding * Scale), first, count));
            (left, top) = Stacked ? (left, top + tall + InnerGap) : (left + width + InnerGap, top);
        }

        return parts;
    }

    private double SpanWidth(int first, int count) =>
        (PaintSizes.InnerPadding * 2 * Scale) + ItemWidths.Skip(first).Take(count).Sum() + (PaintSizes.ItemGap * Scale * Math.Max(0, count - 1));
}
