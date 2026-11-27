using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Thermi.App.Painting;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Windows.Painting;

/// <summary>
/// Draws what Core's <see cref="OverlayLook"/> describes, and nothing it does not. The same code serves the screen and
/// `thermi preview`. Sizes are in device-independent pixels, times the monitor scale. Every GDI+ object is released where
/// it is made, except the fonts, which are kept until the style or the scale changes, and a reused bitmap.
/// </summary>
/// <param name="reuse">
/// Whether each paint draws on the last one's bitmap while its size holds, as the overlay's paints do, a glow's frames
/// included: the painter then owns what it returns, which lasts until its next paint.
/// </param>
internal sealed class Painter(bool reuse = false) : IOverlayPainter
{
    private readonly List<PaintedItem> _places = [];
    private readonly OutlinedTexts _outlines = new();
    private PainterFonts? _fonts;
    private Bitmap? _canvas;

    /// <summary>
    /// Where the last paint drew each item, its label and its value, in its pixels, so the stage can tell what a click is
    /// on (<see cref="PaintedPlaces"/>).
    /// </summary>
    public IReadOnlyList<PaintedItem> Places => _places;

    /// <summary>The last scale <see cref="PainterFit.Fitting"/> found, and what it measured.</summary>
    public ((float Scale, (int Width, int Height)? Most, OverlayLook Look, string Items) Measured, float Scale)? Fitted { get; set; }

    /// <summary>
    /// Paints the items in their pills. <paramref name="glow"/> gives how strongly an item's pill glows now, 0 to 1, as
    /// Core's <see cref="CriticalGlow"/> says; without it nothing glows.
    /// </summary>
    public Bitmap Paint(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, Func<string, double>? glow = null)
    {
        var fonts = FontsFor(look, scale);
        using var measure = Graphics.FromHwnd(0);
        return Painted(PaintLayout.PillsOf(items, look, scale, WidthOn(measure, fonts)), look, scale, glow, mark: false);
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
        return Painted(PaintLayout.StoppedPills(text, look, scale, WidthOn(measure, fonts), beside ?? []), look, scale, glow: null, mark: true);
    }

    public PaintedPicture Picture(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale) => Png(() => Paint(items, look, scale));

    public PaintedPicture Stopped(string text, OverlayLook look, float scale, IReadOnlyList<OverlayItem> beside) =>
        Png(() => PaintStopped(text, look, scale, beside));

    // A bitmap this painter reuses is its own, and stays; one it made for the picture goes with it.
    private PaintedPicture Png(Func<Bitmap> paint)
    {
        var bitmap = paint();
        try
        {
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return new PaintedPicture(png.ToArray(), bitmap.Width, bitmap.Height);
        }
        finally
        {
            if (!reuse)
            {
                bitmap.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _fonts?.Dispose();
        _canvas?.Dispose();
        _outlines.Dispose();
    }

    /// <summary>
    /// The size <see cref="Paint"/> draws the items at, without drawing them: each value keeps the room of its widest
    /// form, so this is the room the overlay keeps however its numbers change.
    /// </summary>
    public Size Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale)
    {
        var fonts = FontsFor(look, scale);
        using var measure = Graphics.FromHwnd(0);
        var (width, height, _) = PaintLayout.SizeOf(PaintLayout.PillsOf(items, look, scale, WidthOn(measure, fonts)), look, scale);
        return new Size(width, height);
    }

    private static TextWidth WidthOn(Graphics g, PainterFonts fonts) => (text, role) =>
    {
        var font = role switch
        {
            TextRole.Label => fonts.Label,
            TextRole.Tag => fonts.Tag,
            TextRole.Value or _ => fonts.Value,
        };
        return g.MeasureString(text, font, 0, PainterFonts.Typographic).Width;
    };

    private Bitmap Painted(List<PaintedPill> pills, OverlayLook look, float scale, Func<string, double>? glow, bool mark)
    {
        var (width, height, arranged) = PaintLayout.SizeOf(pills, look, scale);
        var bitmap = Canvas(width, height);
        try
        {
            var boxes = arranged.Pills.Select(box => box.Rect()).ToList();
            DrawPills(bitmap, pills, boxes, look, scale);

            // The stopped pill's place goes first, as its tooltip does.
            DrawItems(bitmap, mark ? pills[1..] : pills, mark ? boxes[1..] : boxes, look, scale, glow);
            if (mark)
            {
                DrawStopped(bitmap, pills[0].Items[0].Value, boxes[0], look, scale);
            }

            Alpha.Scale(bitmap, look.Opacity);
            _outlines.Painted();
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
    private static void DrawPills(Bitmap bitmap, List<PaintedPill> pills, List<RectangleF> boxes, OverlayLook look, float scale)
    {
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (var p = 0; p < pills.Count; p++)
            {
                PainterShapes.Pill(g, look, boxes[p], scale, (float)Math.Ceiling(look.Height * scale));
                var inner = pills[p].Runs is null ? [] : pills[p].PartsIn(boxes[p].Box());
                foreach (var part in inner)
                {
                    PainterShapes.InnerPill(g, look, part.Box.Rect(), scale);
                }
            }
        }

        Alpha.Scale(bitmap, look.Background);
    }

    // The glow is drawn over the faded pill, so it shows whole at any background opacity.
    private void DrawItems(Bitmap bitmap, List<PaintedPill> pills, List<RectangleF> boxes, OverlayLook look, float scale, Func<string, double>? glow)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _places.Clear();
        for (var p = 0; p < pills.Count; p++)
        {
            foreach (var part in pills[p].PartsIn(boxes[p].Box()))
            {
                var items = pills[p].Items.Skip(part.First).Take(part.Count).ToList();
                if (glow is not null && items.Max(item => glow(item.Id)) is > 0 and var strength)
                {
                    PainterShapes.Glow(g, look, part.Box.Rect(), scale, strength);
                }

                foreach (var drawn in PaintLayout.ItemsIn(pills[p], part, boxes[p].Box(), look, scale, WidthOn(g, _fonts!)))
                {
                    DrawItem(g, drawn, look, scale);
                    _places.Add(drawn.Place);
                }
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

    private void DrawItem(Graphics g, ItemDrawn drawn, OverlayLook look, float scale)
    {
        var (fonts, item, y) = (_fonts!, drawn.Item, (float)drawn.Middle);
        if (item.Label is not null)
        {
            DrawText(g, item.Label, fonts.Label, look.LabelColor.Color, new PointF((float)drawn.LabelX, y), look, scale);
        }

        if (item.Graph is { } graph)
        {
            PainterShapes.Graph(g, graph, look, new PointF((float)drawn.MeterX, y), scale);
        }
        else if (item.Lit is { } lit)
        {
            PainterShapes.Meter(g, lit, look.MeterColor(item.Level), look, new PointF((float)drawn.MeterX, y), scale);
        }

        DrawValue(g, item, fonts.Value, look, new PointF((float)drawn.ValueX, y), scale);
        if (item.Tag is { } tag)
        {
            DrawTag(g, tag, fonts, look, new PointF((float)drawn.TagX, y), scale);
        }
    }

    // On the value's baseline, so the two read as one line however small the tag is.
    private void DrawTag(Graphics g, string tag, PainterFonts fonts, OverlayLook look, PointF at, float scale)
    {
        var value = g.MeasureString(tag, fonts.Value, 0, PainterFonts.Typographic).Height;
        var small = g.MeasureString(tag, fonts.Tag, 0, PainterFonts.Typographic).Height;
        var baseline = at.Y - (value / 2) + Ascent(fonts.Value);
        DrawText(g, tag, fonts.Tag, look.LabelColor.Color, new PointF(at.X, baseline - Ascent(fonts.Tag) + (small / 2)), look, scale);
    }

    private static float Ascent(Font font) => font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);

    // A warning underlines the value, unless its meter or graph shows it, and a critical value is drawn reversed, on a box
    // of its color, where the text effect has nothing to set apart.
    private void DrawValue(Graphics g, OverlayItem item, Font font, OverlayLook look, PointF at, float scale)
    {
        var mark = LevelMarks.Of(look, item);
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

        if (mark.Kind == MarkKind.Underline)
        {
            var line = new RectangleF(text.Left, text.Top + Ascent(font) + (PaintSizes.UnderlineGap * scale), text.Width, PaintSizes.UnderlineHeight * scale);
            PainterShapes.Underline(g, mark.Color, line);
        }
    }

    private void DrawStopped(Bitmap bitmap, string text, RectangleF box, OverlayLook look, float scale)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var drawn = PaintLayout.StoppedIn(box.Box(), scale);
        var middle = (float)drawn.Middle;
        PainterShapes.Mark(g, look.LabelColor.Color, new PointF((float)drawn.MarkX, middle), scale);
        DrawText(g, text, _fonts!.Label, look.LabelColor.Color, new PointF((float)drawn.TextX, middle), look, scale);
        _places.Insert(0, drawn.Place);
    }

    private void DrawText(Graphics g, string text, Font font, Rgb color, PointF at, OverlayLook look, float scale)
    {
        var size = g.MeasureString(text, font, 0, PainterFonts.Typographic);
        var origin = new PointF(at.X, at.Y - (size.Height / 2));
        if (look.Effect == TextEffect.Outline)
        {
            _outlines.Draw(g, text, font, color, look.OutlineColor, 2 * (float)OverlayLook.OutlineWidth * scale, origin);
            return;
        }

        using var brush = new SolidBrush(PainterShapes.Drawn(color));

        if (look.Effect == TextEffect.Shadow)
        {
            using var shadow = new SolidBrush(Color.FromArgb(PaintSizes.ShadowAlpha, PainterShapes.Drawn(look.Pills.Outer)));
            g.DrawString(text, font, shadow, origin.X + scale, origin.Y + scale, PainterFonts.Typographic);
        }

        g.DrawString(text, font, brush, origin.X, origin.Y, PainterFonts.Typographic);
    }
}
