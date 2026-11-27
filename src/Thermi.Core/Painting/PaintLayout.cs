using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.Core.Painting;

/// <summary>Which of a look's fonts a text is drawn in.</summary>
public enum TextRole
{
    Label,
    Value,
    Tag,
}

/// <summary>How wide <paramref name="text"/> draws in the font of <paramref name="role"/>, in the paint's pixels.</summary>
public delegate double TextWidth(string text, TextRole role);

/// <summary>
/// Where one item draws in its pill, in the paint's pixels, each part on the line through <see cref="Middle"/>: its label,
/// its meter or graph, its value and its tag; and the place a press on it finds.
/// </summary>
public sealed record ItemDrawn(OverlayItem Item, double Middle, double LabelX, double MeterX, double ValueX, double TagX, PaintedItem Place);

/// <summary>Where the stopped pill draws thermi's mark and its text, on the line through <see cref="Middle"/>.</summary>
public readonly record struct StoppedDrawn(double MarkX, double TextX, double Middle, PaintedItem Place);

/// <summary>
/// Where every painter puts the pills and what is in them, from the widths its own text engine measures: each OS's painter
/// only draws text, shapes and pixels where this says.
/// </summary>
public static class PaintLayout
{
    // Enough that a round pill's click reaches all but a sliver of what it draws, few enough to stay a handful of actors.
    private const int Bands = 6;

    /// <summary>
    /// The pills <paramref name="items"/> draw in. Each value keeps the room of its widest form, so the overlay keeps its
    /// size however its numbers change. The runs a single bar holds are read before the labels go, as no label then says
    /// where each starts.
    /// </summary>
    public static List<PaintedPill> PillsOf(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale, TextWidth width)
    {
        var runs = OverlayLayout.InnerPills(items, look.Layout);
        var up = runs is not null && look.Orientation == Orientation.Vertical;
        return [.. OverlayLayout.Groups(items, look.Layout, look.Labels)
            .Select(pill => new PaintedPill(pill, [.. pill.Select(item => ItemWidth(width, item, look, scale) * scale)], scale)
            {
                Runs = runs,
                Stacked = up,
            })];
    }

    /// <summary>
    /// The one pill that stands in for every item the sensor service reads while it is stopped: thermi's mark, then
    /// <paramref name="said"/>; the items <paramref name="beside"/> it, which never wait on the service, follow in pills of their own.
    /// </summary>
    public static List<PaintedPill> StoppedPills(string said, OverlayLook look, double scale, TextWidth width, IReadOnlyList<OverlayItem> beside)
    {
        var room = (width(said, TextRole.Label) / scale) + PaintSizes.MarkWidth + PaintSizes.LabelGap;
        var item = new OverlayItem(null, said, Level.Normal, said);
        return [new PaintedPill([item], [room * scale], scale), .. PillsOf(beside, look, scale, width)];
    }

    /// <summary>The picture's size, at least one pill high and wide, and where each pill goes in it.</summary>
    public static (int Width, int Height, Arrangement Arranged) SizeOf(IReadOnlyList<PaintedPill> pills, OverlayLook look, double scale)
    {
        var height = Math.Ceiling(look.Height * scale);
        var arranged = look.Arrange([.. pills.Select(pill => pill.Width)], height, scale, [.. pills.Select(pill => pill.HeightOf(height))]);
        return (Math.Max((int)height, (int)Math.Ceiling(arranged.Width)), Math.Max((int)height, (int)Math.Ceiling(arranged.Height)), arranged);
    }

    /// <summary>
    /// Where each item of <paramref name="part"/> draws, in <paramref name="pill"/> at <paramref name="outer"/>. Each item's
    /// place is as tall as its pill, or its row of a standing bar, so a drag of a run carries it off.
    /// </summary>
    public static IEnumerable<ItemDrawn> ItemsIn(PaintedPill pill, PillPart part, PillBox outer, OverlayLook look, double scale, TextWidth width)
    {
        var gap = PaintSizes.ItemGap * scale;
        var box = pill.Stacked ? part.Box : outer;
        var middle = box.Top + (box.Height / 2);
        var x = part.Start;
        for (var i = part.First; i < part.First + part.Count; i++)
        {
            var (item, room) = (pill.Items[i], pill.ItemWidths[i]);
            var meter = item.Label is null ? x : x + width(item.Label, TextRole.Label) + (PaintSizes.LabelGap * scale);
            var value = meter + (PaintSizes.MeterRoom(item, look) * scale);
            var reversed = LevelMarks.Of(look, item).Kind == MarkKind.Reversed ? PaintSizes.ReversedPadding * scale : 0;
            var tag = value + width(item.Value, TextRole.Value) + reversed + (PaintSizes.TagGap * scale);
            var place = new PillBox(x - (gap / 2), box.Top, room + gap, box.Height);
            var alone = item.Label is null ? place.Left : meter - (PaintSizes.LabelGap * scale / 2);
            var at = new PaintedItem(item.Id, place, new PillBox(value, box.Top, x + room - value, box.Height))
            {
                Pill = part.Box,
                Label = item.Label is null ? default : new PillBox(place.Left, box.Top, alone - place.Left, box.Height),
                Alone = PillBox.FromEdges(alone, place.Top, place.Right, place.Bottom),
            };
            yield return new ItemDrawn(item, middle, x, meter, value, tag, at);
            x += room + gap;
        }
    }

    /// <summary>Where the stopped pill at <paramref name="box"/> draws its mark and its text; its place is the whole pill.</summary>
    public static StoppedDrawn StoppedIn(PillBox box, double scale)
    {
        var mark = box.Left + ((PaintSizes.OuterPadding + PaintSizes.InnerPadding) * scale);
        var text = mark + ((PaintSizes.MarkWidth + PaintSizes.LabelGap) * scale);
        return new StoppedDrawn(mark, text, box.Top + (box.Height / 2), new PaintedItem("", box, box) { Pill = box });
    }

    /// <summary>
    /// Boxes that together cover <paramref name="pill"/> with corners of <paramref name="radius"/> and nothing outside it,
    /// in a few bands, each as wide as the curve lets it at its edge: a host that takes clicks only in boxes passes a click
    /// where the pill draws nothing, its corners included.
    /// </summary>
    public static IEnumerable<PillBox> Taking(PillBox pill, double radius)
    {
        var bands = radius > 0 ? Bands : 1;
        var tall = pill.Height / bands;
        for (var band = 0; band < bands; band++)
        {
            var top = pill.Top + (band * tall);
            var edge = Math.Min(top - pill.Top, pill.Bottom - top - tall);
            var inset = edge >= radius ? 0 : radius - Math.Sqrt((radius * radius) - ((radius - edge) * (radius - edge)));
            if (pill.Width > 2 * inset)
            {
                yield return new PillBox(pill.Left + inset, top, pill.Width - (2 * inset), tall);
            }
        }
    }

    // In unscaled pixels.
    private static double ItemWidth(TextWidth width, OverlayItem item, OverlayLook look, double scale)
    {
        var label = item.Label is null ? 0 : (width(item.Label, TextRole.Label) / scale) + PaintSizes.LabelGap;
        var value = Math.Max(width(item.Value, TextRole.Value), width(item.Room, TextRole.Value)) / scale;
        var tag = item.Tag is null ? 0 : PaintSizes.TagGap + PaintSizes.ReversedPadding + (width(item.Tag, TextRole.Tag) / scale);
        return label + value + tag + PaintSizes.MeterRoom(item, look);
    }
}
