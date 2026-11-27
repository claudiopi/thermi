using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Linux.Painting;

/// <summary>
/// Draws what Core's <see cref="OverlayLook"/> describes with Cairo and Pango, as Windows' painter does with GDI+, from
/// Core's sizes and shapes, and nothing it does not. The same code serves the screen and <c>thermi preview</c>. Sizes are
/// in pixels at 100 %, times the monitor's scale. Its fonts are kept until the style or the scale changes.
/// </summary>
internal sealed class Painter : IDisposable
{
    private const double LabelGap = PaintSizes.LabelGap;

    private readonly List<PaintedItem> _places = [];

    // Text is measured on a context of its own, whose layout draws it too, so what is measured is what is drawn.
    private readonly Cairo.ImageSurface _measureSurface = new(Cairo.Format.Argb32, 1, 1);
    private readonly Cairo.Context _measure;
    private PainterText? _text;
    private (StyleFont, TextStyle, TextStyle, double)? _textKey;

    public Painter()
    {
        _measure = new Cairo.Context(_measureSurface);
    }

    /// <summary>Where the last paint drew each item, its label and its value, in its pixels, so a host can tell what a press is on.</summary>
    public IReadOnlyList<PaintedItem> Places => _places;

    /// <summary>
    /// Paints the items in their pills. <paramref name="glow"/> gives how strongly an item's pill glows now, 0 to 1, as
    /// Core's <see cref="CriticalGlow"/> says; without it nothing glows.
    /// </summary>
    public Picture Paint(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale, Func<string, double>? glow = null)
    {
        var text = TextFor(look, scale);
        return Painted(PillsOf(items, look, scale, text), look, scale, glow, mark: false);
    }

    /// <summary>
    /// The one pill that stands in for every item the sensor service reads while it is stopped: thermi's mark, then
    /// <paramref name="said"/>; the items <paramref name="beside"/> it, which never wait on the service, follow in pills of their own.
    /// </summary>
    public Picture PaintStopped(string said, OverlayLook look, double scale, IReadOnlyList<OverlayItem>? beside = null)
    {
        var text = TextFor(look, scale);
        var width = (text.Measure(said, text.Label).Width / scale) + PaintSizes.MarkWidth + LabelGap;
        var item = new OverlayItem(null, said, Level.Normal, said);
        List<PaintedPill> pills = [new PaintedPill([item], [width * scale], scale), .. PillsOf(beside ?? [], look, scale, text)];
        return Painted(pills, look, scale, glow: null, mark: true);
    }

    /// <summary>
    /// The size <see cref="Paint"/> draws the items at, without drawing them: each value keeps the room of its widest form,
    /// so this is the room the overlay keeps however its numbers change.
    /// </summary>
    public (int Width, int Height) Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale)
    {
        var (width, height, _) = SizeOf(PillsOf(items, look, scale, TextFor(look, scale)), look, scale);
        return (width, height);
    }

    public void Dispose()
    {
        _text?.Dispose();
        _measure.Dispose();
        _measureSurface.Dispose();
    }

    // The runs a single bar holds are read before the labels go, as no label then says where each starts.
    private static List<PaintedPill> PillsOf(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale, PainterText text)
    {
        var runs = OverlayLayout.InnerPills(items, look.Layout);
        var up = runs is not null && look.Orientation == Orientation.Vertical;
        return [.. OverlayLayout.Groups(items, look.Layout, look.Labels)
            .Select(pill => new PaintedPill(pill, [.. pill.Select(item => ItemWidth(text, item, look, scale) * scale)], scale)
            {
                Runs = runs,
                Stacked = up,
            })];
    }

    private static (int Width, int Height, Arrangement Arranged) SizeOf(List<PaintedPill> pills, OverlayLook look, double scale)
    {
        var height = Math.Ceiling(look.Height * scale);
        var arranged = look.Arrange([.. pills.Select(pill => pill.Width)], height, scale, [.. pills.Select(pill => pill.HeightOf(height))]);
        return (Math.Max((int)height, (int)Math.Ceiling(arranged.Width)), Math.Max((int)height, (int)Math.Ceiling(arranged.Height)), arranged);
    }

    // The pills fade before anything is drawn on them, so the text and meters stay whole over a faded pill; the glow is
    // drawn over the faded pill, so it shows whole at any background opacity; and the whole fades last.
    private Picture Painted(List<PaintedPill> pills, OverlayLook look, double scale, Func<string, double>? glow, bool mark)
    {
        var (width, height, arranged) = SizeOf(pills, look, scale);
        var picture = new Picture(width, height);
        try
        {
            using (var cr = new Cairo.Context(picture.Surface))
            {
                var boxes = arranged.Pills.ToList();
                cr.PushGroup();
                cr.PushGroup();
                DrawPills(cr, pills, boxes, look, scale);
                cr.PopGroupToSource();
                cr.PaintWithAlpha(look.Background);

                // The stopped pill's place goes first, as its tooltip does.
                _places.Clear();
                DrawItems(cr, mark ? pills[1..] : pills, mark ? boxes[1..] : boxes, look, scale, glow);
                if (mark)
                {
                    DrawStopped(cr, pills[0].Items[0].Value, boxes[0], look, scale);
                }

                cr.PopGroupToSource();
                cr.PaintWithAlpha(look.Opacity);
            }

            return picture;
        }
        catch
        {
            picture.Dispose();
            throw;
        }
    }

    private static void DrawPills(Cairo.Context cr, List<PaintedPill> pills, List<PillBox> boxes, OverlayLook look, double scale)
    {
        for (var p = 0; p < pills.Count; p++)
        {
            PainterShapes.Pill(cr, look, boxes[p], scale, Math.Ceiling(look.Height * scale));
            var inner = pills[p].Runs is null ? [] : pills[p].PartsIn(boxes[p]);
            foreach (var part in inner)
            {
                PainterShapes.InnerPill(cr, look, part.Box, scale);
            }
        }
    }

    private void DrawItems(Cairo.Context cr, List<PaintedPill> pills, List<PillBox> boxes, OverlayLook look, double scale, Func<string, double>? glow)
    {
        for (var p = 0; p < pills.Count; p++)
        {
            foreach (var part in pills[p].PartsIn(boxes[p]))
            {
                var items = pills[p].Items.Skip(part.First).Take(part.Count).ToList();
                if (glow is not null && items.Max(item => glow(item.Id)) is > 0 and var strength)
                {
                    PainterShapes.Glow(cr, look, part.Box, scale, strength);
                }

                DrawPart(cr, pills[p], part, boxes[p], look, scale);
            }
        }
    }

    // Each item's place is as tall as its pill, or its row of a standing bar, so a drag of a run carries it off.
    private void DrawPart(Cairo.Context cr, PaintedPill pill, PillPart part, PillBox outer, OverlayLook look, double scale)
    {
        var gap = PaintSizes.ItemGap * scale;
        var box = pill.Stacked ? part.Box : outer;
        var x = part.Start;
        for (var i = part.First; i < part.First + part.Count; i++)
        {
            var (item, width) = (pill.Items[i], pill.ItemWidths[i]);
            var value = DrawItem(cr, item, look, x, box.Top + (box.Height / 2), scale);
            var place = new PillBox(x - (gap / 2), box.Top, width + gap, box.Height);
            var alone = item.Label is null ? place.Left : value - (PaintSizes.MeterRoom(item, look) * scale) - (LabelGap * scale / 2);
            _places.Add(new PaintedItem(item.Id, place, new PillBox(value, box.Top, x + width - value, box.Height))
            {
                Pill = part.Box,
                Label = item.Label is null ? default : new PillBox(place.Left, box.Top, alone - place.Left, box.Height),
                Alone = PillBox.FromEdges(alone, place.Top, place.Right, place.Bottom),
            });
            x += width + gap;
        }
    }

    private PainterText TextFor(OverlayLook look, double scale)
    {
        var key = (look.Font, look.Label, look.Value, scale);
        if (_text is { } text && _textKey == key)
        {
            text.Look = look;
            return text;
        }

        _text?.Dispose();
        _text = null;
        _text = new PainterText(_measure, look.Font, look.Label, look.Value, scale) { Look = look };
        _textKey = key;
        return _text;
    }

    // In unscaled pixels.
    private static double ItemWidth(PainterText text, OverlayItem item, OverlayLook look, double scale) =>
        LabelWidth(text, item, scale) + ValueWidth(text, item, scale) + PaintSizes.MeterRoom(item, look);

    private static double LabelWidth(PainterText text, OverlayItem item, double scale) =>
        item.Label is null ? 0 : (text.Measure(item.Label, text.Label).Width / scale) + LabelGap;

    private static double ValueWidth(PainterText text, OverlayItem item, double scale)
    {
        var width = Math.Max(text.Measure(item.Value, text.Value).Width, text.Measure(item.Room, text.Value).Width) / scale;
        var tag = item.Tag is null ? 0 : PaintSizes.TagGap + PaintSizes.ReversedPadding + (text.Measure(item.Tag, text.Tag).Width / scale);
        return width + tag;
    }

    // Gives where the value starts.
    private double DrawItem(Cairo.Context cr, OverlayItem item, OverlayLook look, double x, double y, double scale)
    {
        var text = _text!;
        if (item.Label is not null)
        {
            x = text.Draw(cr, item.Label, text.Label, look.LabelColor.Color, x, y) + (LabelGap * scale);
        }

        if (item.Graph is { } graph)
        {
            PainterShapes.Graph(cr, graph, look, x, y, scale);
        }
        else if (item.Lit is { } lit)
        {
            PainterShapes.Meter(cr, lit, look.MeterColor(item.Level), look, x, y, scale);
        }

        x += PaintSizes.MeterRoom(item, look) * scale;
        var end = DrawValue(cr, item, look, x, y, scale);
        if (item.Tag is { } tag)
        {
            DrawTag(cr, tag, look, end + (PaintSizes.TagGap * scale), y);
        }

        return x;
    }

    // On the value's baseline, so the two read as one line however small the tag is.
    private void DrawTag(Cairo.Context cr, string tag, OverlayLook look, double x, double y)
    {
        var text = _text!;
        var value = text.Measure(tag, text.Value);
        var small = text.Measure(tag, text.Tag);
        var baseline = y - (value.Height / 2) + value.Baseline;
        text.Draw(cr, tag, text.Tag, look.LabelColor.Color, x, baseline - small.Baseline + (small.Height / 2));
    }

    // A warning underlines the value, unless its meter or graph shows it, and a critical value is drawn reversed, on a box
    // of its color, where the text effect has nothing to set apart. Gives where the value ends.
    private double DrawValue(Cairo.Context cr, OverlayItem item, OverlayLook look, double x, double y, double scale)
    {
        var text = _text!;
        var mark = LevelMarks.Of(look, item);
        var size = text.Measure(item.Value, text.Value);
        var box = new PillBox(x, y - (size.Height / 2), size.Width, size.Height);
        if (mark.Kind == MarkKind.Reversed)
        {
            PainterShapes.Reversed(cr, mark.Color, box, scale);
            text.Draw(cr, item.Value, text.Value, mark.Text, x, y, plain: true);
        }
        else
        {
            text.Draw(cr, item.Value, text.Value, look.ValueColorOf(item).Color, x, y);
        }

        // Over a reversed value's box too, so it shows at every level.
        if (item.Throttled)
        {
            PainterShapes.Overline(cr, mark.Kind == MarkKind.Reversed ? mark.Text : LevelMarks.Of(look, Level.Critical).Color, box, scale);
        }

        if (mark.Kind == MarkKind.Reversed)
        {
            return box.Right + (PaintSizes.ReversedPadding * scale);
        }

        if (mark.Kind == MarkKind.Underline)
        {
            var line = new PillBox(box.Left, box.Top + size.Baseline + (PaintSizes.UnderlineGap * scale), box.Width, PaintSizes.UnderlineHeight * scale);
            PainterShapes.Underline(cr, mark.Color, line);
        }

        return box.Right;
    }

    private void DrawStopped(Cairo.Context cr, string said, PillBox box, OverlayLook look, double scale)
    {
        var x = box.Left + ((PaintSizes.OuterPadding + PaintSizes.InnerPadding) * scale);
        var middle = box.Top + (box.Height / 2);
        PainterShapes.Mark(cr, look.LabelColor.Color, x, middle, scale);
        x += (PaintSizes.MarkWidth + LabelGap) * scale;
        _text!.Draw(cr, said, _text.Label, look.LabelColor.Color, x, middle);
        _places.Insert(0, new PaintedItem("", box, box) { Pill = box });
    }
}
