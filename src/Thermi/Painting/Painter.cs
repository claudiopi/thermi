using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.Painting;

/// <summary>
/// Draws what Core's <see cref="OverlayLook"/> describes, and nothing it does not. The same code serves the screen and
/// `thermi preview`. Sizes are in device-independent pixels, times the monitor scale. Every GDI+ object is released where
/// it is made, except the fonts, which are kept until the style or the scale changes, and a reused bitmap.
/// </summary>
/// <param name="reuse">
/// Whether each paint draws on the last one's bitmap while its size holds, as the overlay's paints do, a glow's frames
/// included: the painter then owns what it returns, which lasts until its next paint.
/// </param>
internal sealed class Painter(bool reuse = false) : IDisposable
{
    private const float Height = 38;
    private const float InnerPadding = 14;
    private const float LabelGap = 10;
    private const float MeterGap = 9;
    private const float ItemGap = 14;
    private const float UnderlineGap = 2;
    private const float UnderlineHeight = 2;

    // The shadow takes the pill's color, so it sets the text apart from the wallpaper as the pill would.
    private const int ShadowAlpha = 160;

    private readonly List<PaintedItem> _places = [];
    private PainterFonts? _fonts;
    private Bitmap? _canvas;

    /// <summary>Where the last paint drew each item and its value, in its pixels, so the stage can tell what a click is on.</summary>
    public IReadOnlyList<PaintedItem> Places => _places;

    /// <summary>
    /// Paints the items in their pills. <paramref name="glow"/> gives how strongly an item's pill glows now, 0 to 1, as
    /// Core's <see cref="CriticalGlow"/> says; without it nothing glows.
    /// </summary>
    public Bitmap Paint(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, Func<string, double>? glow = null)
    {
        var fonts = FontsFor(look, scale);
        using var measure = Graphics.FromHwnd(0);
        return Painted(PillsOf(items, look, scale, fonts, measure), look, scale, glow, mark: false);
    }

    /// <summary>
    /// The one pill that stands in for every item the sensor service reads while it is stopped: thermi's mark, then
    /// <paramref name="text"/>; the items <paramref name="beside"/> it, which never wait on the service, follow in pills
    /// of their own.
    /// </summary>
    public Bitmap PaintStopped(string text, OverlayLook look, float scale, IReadOnlyList<OverlayItem>? beside = null)
    {
        var fonts = FontsFor(look, scale);
        using var measure = Graphics.FromHwnd(0);
        var width = (measure.MeasureString(text, fonts.Label, 0, PainterFonts.Typographic).Width / scale) + PainterShapes.MarkWidth + LabelGap;
        var item = new OverlayItem(null, text, Level.Normal, text);
        return Painted([new Pill([item], [width * scale], scale), .. PillsOf(beside ?? [], look, scale, fonts, measure)], look, scale, glow: null, mark: true);
    }

    public void Dispose()
    {
        _fonts?.Dispose();
        _canvas?.Dispose();
    }

    private static List<Pill> PillsOf(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, PainterFonts fonts, Graphics measure) =>
        [.. OverlayLayout.Groups(items, look.Layout, look.Labels)
            .Select(pill => new Pill(pill, [.. pill.Select(item => ItemWidth(measure, fonts, item, scale) * scale)], scale))];

    /// <summary>
    /// The width <see cref="Paint"/> draws the items at, without drawing them: each value keeps the room of its widest
    /// form, so this is the room the overlay keeps on a taskbar however its numbers change.
    /// </summary>
    public int Width(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale)
    {
        var fonts = FontsFor(look, scale);
        using var measure = Graphics.FromHwnd(0);
        return SizeOf(PillsOf(items, look, scale, fonts, measure), look, scale).Width;
    }

    private static (int Width, int Height, Arrangement Arranged) SizeOf(List<Pill> pills, OverlayLook look, float scale)
    {
        var height = Math.Ceiling(Height * scale);
        var arranged = look.Arrange([.. pills.Select(pill => (double)pill.Width)], height, scale);
        return (Math.Max((int)height, (int)Math.Ceiling(arranged.Width)), Math.Max((int)height, (int)Math.Ceiling(arranged.Height)), arranged);
    }

    private Bitmap Painted(List<Pill> pills, OverlayLook look, float scale, Func<string, double>? glow, bool mark)
    {
        var size = SizeOf(pills, look, scale);
        var bitmap = Canvas(size.Width, size.Height);
        try
        {
            var boxes = size.Arranged.Pills.Select(box => new RectangleF((float)box.Left, (float)box.Top, (float)box.Width, (float)box.Height)).ToList();
            DrawPills(bitmap, boxes, look, scale);

            // The stopped pill's place goes first, as its tooltip does.
            DrawItems(bitmap, mark ? pills[1..] : pills, mark ? boxes[1..] : boxes, look, scale, glow);
            if (mark)
            {
                DrawStopped(bitmap, pills[0].Items[0].Value, boxes[0], look, scale);
            }

            Alpha.Scale(bitmap, look.Opacity);
            return bitmap;
        }
        catch
        {
            _canvas = bitmap == _canvas ? null : _canvas;
            bitmap.Dispose();
            throw;
        }
    }

    // A reused bitmap is cleared to what a new one holds, every pixel zero, so each paint draws as on a new one.
    private Bitmap Canvas(int width, int height)
    {
        if (!reuse)
        {
            return new Bitmap(width, height, PixelFormat.Format32bppArgb);
        }

        if (_canvas is { } canvas && canvas.Width == width && canvas.Height == height)
        {
            using var g = Graphics.FromImage(canvas);
            g.Clear(Color.FromArgb(0, 0, 0, 0));
            return canvas;
        }

        _canvas?.Dispose();
        _canvas = null;
        _canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        return _canvas;
    }

    // The pills fade before anything is drawn on them, so the text and meters stay whole over a faded pill.
    private static void DrawPills(Bitmap bitmap, List<RectangleF> boxes, OverlayLook look, float scale)
    {
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var box in boxes)
            {
                PainterShapes.Pill(g, look, box, scale);
            }
        }

        Alpha.Scale(bitmap, look.Background);
    }

    // The glow is drawn over the faded pill, so it shows whole at any background opacity.
    private void DrawItems(Bitmap bitmap, List<Pill> pills, List<RectangleF> boxes, OverlayLook look, float scale, Func<string, double>? glow)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _places.Clear();
        var gap = ItemGap * scale;
        for (var p = 0; p < pills.Count; p++)
        {
            if (glow is not null && pills[p].Items.Max(item => glow(item.Id)) is > 0 and var strength)
            {
                PainterShapes.Glow(g, look, boxes[p], scale, strength);
            }

            var x = boxes[p].Left + ((PainterShapes.OuterPadding + InnerPadding) * scale);
            for (var i = 0; i < pills[p].Items.Count; i++)
            {
                var (item, width) = (pills[p].Items[i], pills[p].ItemWidths[i]);
                var value = DrawItem(g, item, look, new PointF(x, boxes[p].Top + (boxes[p].Height / 2)), scale);
                var place = new RectangleF(x - (gap / 2), boxes[p].Top, width + gap, boxes[p].Height);
                _places.Add(new PaintedItem(item.Id, place, new RectangleF(value, boxes[p].Top, x + width - value, boxes[p].Height)) { Pill = boxes[p] });
                x += width + gap;
            }
        }
    }

    private PainterFonts FontsFor(OverlayLook look, float scale)
    {
        if (_fonts is { } fonts && fonts.Key == (look.Font, look.Label, look.Value, scale))
        {
            return fonts;
        }

        _fonts?.Dispose();
        _fonts = null;
        _fonts = new PainterFonts(look.Font, look.Label, look.Value, scale);
        return _fonts;
    }

    // In unscaled pixels.
    private static float ItemWidth(Graphics g, PainterFonts fonts, OverlayItem item, float scale) =>
        LabelWidth(g, fonts, item, scale) + ValueWidth(g, fonts, item, scale);

    private static float LabelWidth(Graphics g, PainterFonts fonts, OverlayItem item, float scale) =>
        item.Label is null ? 0 : (g.MeasureString(item.Label, fonts.Label, 0, PainterFonts.Typographic).Width / scale) + LabelGap;

    private static float ValueWidth(Graphics g, PainterFonts fonts, OverlayItem item, float scale)
    {
        var width = Math.Max(
            g.MeasureString(item.Value, fonts.Value, 0, PainterFonts.Typographic).Width,
            g.MeasureString(item.Room, fonts.Value, 0, PainterFonts.Typographic).Width) / scale;
        return item.Graph is null ? width : width + PainterShapes.GraphWidth + MeterGap;
    }

    // Gives where the value starts.
    private float DrawItem(Graphics g, OverlayItem item, OverlayLook look, PointF at, float scale)
    {
        var fonts = _fonts!;
        var x = at.X;
        if (item.Label is not null)
        {
            x = DrawText(g, item.Label, fonts.Label, look.LabelColor.Color, new PointF(x, at.Y), look, scale) + (LabelGap * scale);
        }

        if (item.Graph is { } graph)
        {
            PainterShapes.Graph(g, graph, look, new PointF(x, at.Y), scale);
            x += (PainterShapes.GraphWidth + MeterGap) * scale;
        }

        DrawValue(g, item, fonts.Value, look, new PointF(x, at.Y), scale);
        return x;
    }

    // A level never rests on color alone: a warning underlines the value, unless its graph shows it, and a critical value
    // is drawn reversed, on a box of its color, where the text effect has nothing to set apart.
    private static void DrawValue(Graphics g, OverlayItem item, Font font, OverlayLook look, PointF at, float scale)
    {
        var mark = LevelMarks.Of(look, item.Level, item.Graph is not null);
        var size = g.MeasureString(item.Value, font, 0, PainterFonts.Typographic);
        var text = new RectangleF(at.X, at.Y - (size.Height / 2), size.Width, size.Height);
        if (mark.Kind == MarkKind.Reversed)
        {
            PainterShapes.Reversed(g, mark.Color, text, scale);
            DrawText(g, item.Value, font, mark.Text, at, look with { Effect = TextEffect.None }, scale);
        }
        else
        {
            DrawText(g, item.Value, font, look.ValueColorOf(item).Color, at, look, scale);
        }

        // Over a reversed value's box too, so it shows at every level.
        if (item.Throttled)
        {
            PainterShapes.Overline(g, mark.Kind == MarkKind.Reversed ? mark.Text : LevelMarks.Of(look, Level.Critical).Color, text, scale);
        }

        if (mark.Kind == MarkKind.Reversed)
        {
            return;
        }

        if (mark.Kind == MarkKind.Underline)
        {
            var baseline = text.Top + (font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style));
            PainterShapes.Underline(g, mark.Color, new RectangleF(text.Left, baseline + (UnderlineGap * scale), text.Width, UnderlineHeight * scale));
        }
    }

    private void DrawStopped(Bitmap bitmap, string text, RectangleF box, OverlayLook look, float scale)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var x = box.Left + ((PainterShapes.OuterPadding + InnerPadding) * scale);
        var middle = box.Top + (box.Height / 2);
        PainterShapes.Mark(g, look.LabelColor.Color, new PointF(x, middle), scale);
        x += (PainterShapes.MarkWidth + LabelGap) * scale;
        DrawText(g, text, _fonts!.Label, look.LabelColor.Color, new PointF(x, middle), look, scale);
        _places.Insert(0, new PaintedItem("", box, box) { Pill = box });
    }

    // The outline is the text's own shape stroked and then filled, so the two always line up.
    private static float DrawText(Graphics g, string text, Font font, Rgb color, PointF at, OverlayLook look, float scale)
    {
        var size = g.MeasureString(text, font, 0, PainterFonts.Typographic);
        var origin = new PointF(at.X, at.Y - (size.Height / 2));
        using var brush = new SolidBrush(PainterShapes.Drawn(color));
        if (look.Effect == TextEffect.Outline)
        {
            using var path = new GraphicsPath();
            using var pen = new Pen(PainterShapes.Drawn(look.OutlineColor), 2 * (float)OverlayLook.OutlineWidth * scale) { LineJoin = LineJoin.Round };
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, origin, PainterFonts.Typographic);
            g.DrawPath(pen, path);
            g.FillPath(brush, path);
            return at.X + size.Width;
        }

        if (look.Effect == TextEffect.Shadow)
        {
            using var shadow = new SolidBrush(Color.FromArgb(ShadowAlpha, PainterShapes.Drawn(look.Pills.Outer)));
            g.DrawString(text, font, shadow, origin.X + scale, origin.Y + scale, PainterFonts.Typographic);
        }

        g.DrawString(text, font, brush, origin.X, origin.Y, PainterFonts.Typographic);
        return at.X + size.Width;
    }

    private sealed record Pill(IReadOnlyList<OverlayItem> Items, List<float> ItemWidths, float Scale)
    {
        public float Width { get; } = ((PainterShapes.OuterPadding + InnerPadding) * 2 * Scale)
            + ItemWidths.Sum()
            + (ItemGap * Scale * Math.Max(0, ItemWidths.Count - 1));
    }
}

/// <summary>Where an item was drawn, half the gap to its neighbors included, and where its value was, in the bitmap's pixels.</summary>
internal sealed record PaintedItem(string Id, RectangleF Item, RectangleF Value)
{
    /// <summary>The pill the item was drawn in, which a drag of it carries off.</summary>
    public RectangleF Pill { get; init; }
}
