using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Linux.Painting;

/// <summary>
/// Draws what Core's <see cref="OverlayLook"/> describes with Cairo and Pango, as Windows' painter does with GDI+, where
/// Core's <see cref="PaintLayout"/> places it, and nothing it does not. The same code serves the screen and <c>thermi preview</c>.
/// Sizes are in pixels at 100 %, times the monitor's scale. Its fonts are kept until the style or the scale changes.
/// </summary>
internal sealed class Painter : IDisposable
{
    private readonly List<PaintedItem> _places = [];

    // Whether each paint draws on the last one's surface while its size holds, as the overlay's paints do: the painter
    // then owns what it returns, which lasts until its next paint.
    private readonly bool _reuse;
    private Picture? _canvas;

    // Text is measured on a context of its own, whose layout draws it too, so what is measured is what is drawn.
    private readonly Cairo.ImageSurface _measureSurface = new(Cairo.Format.Argb32, 1, 1);
    private readonly Cairo.Context _measure;
    private PainterText? _text;
    private (StyleFont, TextStyle, TextStyle, double)? _textKey;

    public Painter(bool reuse = false)
    {
        _reuse = reuse;
        _measure = new Cairo.Context(_measureSurface);
    }

    /// <summary>Where the last paint drew each item, its label and its value, in its pixels, so a host can tell what a press is on.</summary>
    public IReadOnlyList<PaintedItem> Places => _places;

    /// <summary>Where the last paint drew something to click, in its pixels: its pills, in the shape it drew them.</summary>
    public IReadOnlyList<PillBox> Clickable { get; private set; } = [];

    /// <summary>
    /// Paints the items in their pills. <paramref name="glow"/> gives how strongly an item's pill glows now, 0 to 1, as
    /// Core's <see cref="CriticalGlow"/> says; without it nothing glows.
    /// </summary>
    public Picture Paint(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale, Func<string, double>? glow = null)
    {
        var text = TextFor(look, scale);
        return Painted(PaintLayout.PillsOf(items, look, scale, text.Width), look, scale, glow, mark: false);
    }

    /// <summary>
    /// The one pill that stands in for every item the sensor service reads while it is stopped: thermi's mark, then
    /// <paramref name="said"/>; the items <paramref name="beside"/> it, which never wait on the service, follow in pills of their own.
    /// </summary>
    public Picture PaintStopped(string said, OverlayLook look, double scale, IReadOnlyList<OverlayItem>? beside = null)
    {
        var text = TextFor(look, scale);
        return Painted(PaintLayout.StoppedPills(said, look, scale, text.Width, beside ?? []), look, scale, glow: null, mark: true);
    }

    /// <summary>
    /// The size <see cref="Paint"/> draws the items at, without drawing them: each value keeps the room of its widest form,
    /// so this is the room the overlay keeps however its numbers change.
    /// </summary>
    public (int Width, int Height) Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, double scale)
    {
        var (width, height, _) = PaintLayout.SizeOf(PaintLayout.PillsOf(items, look, scale, TextFor(look, scale).Width), look, scale);
        return (width, height);
    }

    public void Dispose()
    {
        _text?.Dispose();
        _measure.Dispose();
        _measureSurface.Dispose();
        _canvas?.Dispose();
    }

    // The pills fade before anything is drawn on them, so the text and meters stay whole over a faded pill; the glow is
    // drawn over the faded pill, so it shows whole at any background opacity; and the whole fades last.
    private Picture Painted(List<PaintedPill> pills, OverlayLook look, double scale, Func<string, double>? glow, bool mark)
    {
        var (width, height, arranged) = PaintLayout.SizeOf(pills, look, scale);
        var picture = Canvas(width, height);
        try
        {
            using (var cr = new Cairo.Context(picture.Surface))
            {
                var boxes = arranged.Pills.ToList();
                var row = Math.Ceiling(look.Height * scale);
                Clickable = [.. boxes.SelectMany(box => PaintLayout.Taking(PainterShapes.Outline(box), look.Radius(Math.Min(box.Height, row) - 1)))];
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
            _canvas = picture == _canvas ? null : _canvas;
            picture.Dispose();
            throw;
        }
    }

    // A reused surface is cleared to what a new one holds, every pixel zero, so each paint draws as on a new one.
    private Picture Canvas(int width, int height)
    {
        if (!_reuse)
        {
            return new Picture(width, height);
        }

        if (_canvas is { } canvas && canvas.Width == width && canvas.Height == height)
        {
            canvas.Clear();
            return canvas;
        }

        _canvas?.Dispose();
        _canvas = null;
        _canvas = new Picture(width, height);
        return _canvas;
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

                foreach (var drawn in PaintLayout.ItemsIn(pills[p], part, boxes[p], look, scale, _text!.Width))
                {
                    DrawItem(cr, drawn, look, scale);
                    _places.Add(drawn.Place);
                }
            }
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

    private void DrawItem(Cairo.Context cr, ItemDrawn drawn, OverlayLook look, double scale)
    {
        var (text, item, y) = (_text!, drawn.Item, drawn.Middle);
        if (item.Label is not null)
        {
            text.Draw(cr, item.Label, text.Label, look.LabelColor.Color, drawn.LabelX, y);
        }

        if (item.Graph is { } graph)
        {
            PainterShapes.Graph(cr, graph, look, drawn.MeterX, y, scale);
        }
        else if (item.Lit is { } lit)
        {
            PainterShapes.Meter(cr, lit, look.MeterColor(item.Level), look, drawn.MeterX, y, scale);
        }

        DrawValue(cr, item, look, drawn.ValueX, y, scale);
        if (item.Tag is { } tag)
        {
            DrawTag(cr, tag, look, drawn.TagX, y);
        }
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
    // of its color, where the text effect has nothing to set apart.
    private void DrawValue(Cairo.Context cr, OverlayItem item, OverlayLook look, double x, double y, double scale)
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

        if (mark.Kind == MarkKind.Underline)
        {
            var line = new PillBox(box.Left, box.Top + size.Baseline + (PaintSizes.UnderlineGap * scale), box.Width, PaintSizes.UnderlineHeight * scale);
            PainterShapes.Underline(cr, mark.Color, line);
        }
    }

    private void DrawStopped(Cairo.Context cr, string said, PillBox box, OverlayLook look, double scale)
    {
        var drawn = PaintLayout.StoppedIn(box, scale);
        PainterShapes.Mark(cr, look.LabelColor.Color, drawn.MarkX, drawn.Middle, scale);
        _text!.Draw(cr, said, _text.Label, look.LabelColor.Color, drawn.TextX, drawn.Middle);
        _places.Insert(0, drawn.Place);
    }
}
