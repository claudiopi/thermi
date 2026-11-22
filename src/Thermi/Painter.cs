using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Thermi.Core;

namespace Thermi;

/// <summary>
/// The same code serves the screen and `thermi preview`. Sizes are in device-independent pixels, times the monitor scale.
/// </summary>
internal sealed class Painter : IDisposable
{
    private const float Height = 38;
    private const float OuterPadding = 4;
    private const float InnerPadding = 14;
    private const float LabelGap = 10;
    private const float MeterWidth = 17;
    private const float MeterGap = 9;
    private const float ItemGap = 14;
    private const float PillGap = 8;

    private static readonly Color Outer = ColorTranslator.FromHtml("#20201E");
    private static readonly Color OuterBorder = ColorTranslator.FromHtml("#31312F");
    private static readonly Color Inner = ColorTranslator.FromHtml("#171715");
    private static readonly Color LabelColor = ColorTranslator.FromHtml("#A9A9A7");
    private static readonly Color ValueColor = ColorTranslator.FromHtml("#EFEFEE");
    private static readonly Color MeterOff = ColorTranslator.FromHtml("#2E2E2C");
    private static readonly Color Normal = ColorTranslator.FromHtml("#3DC86A");
    private static readonly Color Warning = ColorTranslator.FromHtml("#FEC84B");
    private static readonly Color Critical = ColorTranslator.FromHtml("#F05A50");

    // Segoe UI Variable is the Windows 11 face; Windows 10 has only Segoe UI.
    private static readonly string[] LabelFamilies = ["Segoe UI Variable Text Semibold", "Segoe UI Semibold"];
    private static readonly string[] ValueFamilies = ["Segoe UI Variable Display", "Segoe UI"];

    private readonly StringFormat _format = (StringFormat)StringFormat.GenericTypographic.Clone();
    private Font? _label;
    private Font? _value;
    private float _fontScale;

    public Bitmap Paint(IReadOnlyList<OverlayItem> items, float scale)
    {
        EnsureFonts(scale);
        using var measure = Graphics.FromHwnd(0);
        var pills = OverlayLayout.Groups(items).Select(pill => new Pill(pill, [.. pill.Select(item => ItemWidth(measure, item) * scale)], scale)).ToList();
        var height = (int)Math.Ceiling(Height * scale);
        var width = Math.Max(height, (int)Math.Ceiling(pills.Sum(pill => pill.Width) + (PillGap * scale * Math.Max(0, pills.Count - 1))));

        Bitmap? bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            Draw(bitmap, pills, scale);
            var painted = bitmap;
            bitmap = null;
            return painted;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    public void Dispose()
    {
        _format.Dispose();
        _label?.Dispose();
        _value?.Dispose();
    }

    private void Draw(Bitmap bitmap, List<Pill> pills, float scale)
    {
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var left = 0f;
        foreach (var pill in pills)
        {
            DrawBackground(g, left, pill.Width, bitmap.Height, scale);
            var x = left + ((OuterPadding + InnerPadding) * scale);
            for (var i = 0; i < pill.Items.Count; i++)
            {
                DrawItem(g, pill.Items[i], x, bitmap.Height / 2f, scale);
                x += pill.ItemWidths[i] + (ItemGap * scale);
            }

            left += pill.Width + (PillGap * scale);
        }
    }

    private void EnsureFonts(float scale)
    {
        if (_label is not null && Math.Abs(_fontScale - scale) < 0.01)
        {
            return;
        }

        _label?.Dispose();
        _value?.Dispose();
        _label = new Font(Family(LabelFamilies), 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _value = new Font(Family(ValueFamilies), 13.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _fontScale = scale;
    }

    // In unscaled pixels.
    private float ItemWidth(Graphics g, OverlayItem item)
    {
        var width = Math.Max(g.MeasureString(item.Value, _value!, 0, _format).Width, g.MeasureString(item.Room, _value!, 0, _format).Width) / _fontScale;
        if (item.Label is not null)
        {
            width += (g.MeasureString(item.Label, _label!, 0, _format).Width / _fontScale) + LabelGap;
        }

        return item.Fill is null ? width : width + MeterWidth + MeterGap;
    }

    private static void DrawBackground(Graphics g, float left, float width, int height, float scale)
    {
        using var outerPath = Rounded(new RectangleF(left + 0.5f, 0.5f, width - 1, height - 1), (height - 1) / 2f);
        using var outerBrush = new SolidBrush(Outer);
        using var border = new Pen(OuterBorder, Math.Max(1, scale));
        g.FillPath(outerBrush, outerPath);
        g.DrawPath(border, outerPath);

        var pad = OuterPadding * scale;
        using var innerPath = Rounded(new RectangleF(left + pad, pad, width - (2 * pad), height - (2 * pad)), (height - (2 * pad)) / 2f);
        using var innerBrush = new SolidBrush(Inner);
        g.FillPath(innerBrush, innerPath);
    }

    private void DrawItem(Graphics g, OverlayItem item, float x, float middle, float scale)
    {
        if (item.Label is not null)
        {
            x = DrawText(g, item.Label, _label!, LabelColor, x, middle) + (LabelGap * scale);
        }

        if (item.Fill is { } fill)
        {
            DrawMeter(g, fill, item.Level, x, middle, scale);
            x += (MeterWidth + MeterGap) * scale;
        }

        DrawText(g, item.Value, _value!, ValueColor, x, middle);
    }

    private float DrawText(Graphics g, string text, Font font, Color color, float x, float middle)
    {
        var size = g.MeasureString(text, font, 0, _format);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, middle - (size.Height / 2), _format);
        return x + size.Width;
    }

    private static void DrawMeter(Graphics g, double fill, Level level, float x, float middle, float scale)
    {
        const int Bars = 5;
        var lit = (int)Math.Ceiling(fill * Bars);
        var color = level switch
        {
            Level.Critical => Critical,
            Level.Warning => Warning,
            Level.Normal => Normal,
            _ => Normal,
        };
        using var on = new SolidBrush(color);
        using var off = new SolidBrush(MeterOff);
        var barHeight = 2 * scale;
        var step = 3 * scale;
        var top = middle - (((Bars * step) - scale) / 2);
        for (var bar = 0; bar < Bars; bar++)
        {
            var fromBottom = Bars - 1 - bar;
            g.FillRectangle(fromBottom < lit ? on : off, x, top + (bar * step), MeterWidth * scale, barHeight);
        }
    }

    private static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 90, 180);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }

    private sealed record Pill(IReadOnlyList<OverlayItem> Items, List<float> ItemWidths, float Scale)
    {
        public float Width { get; } = ((OuterPadding + InnerPadding) * 2 * Scale) + ItemWidths.Sum() + (ItemGap * Scale * Math.Max(0, ItemWidths.Count - 1));
    }

    // GDI+ silently swaps a missing family for Microsoft Sans Serif, so the choice is made here.
    private static string Family(string[] families)
    {
        using var installed = new InstalledFontCollection();
        var present = installed.Families;
        try
        {
            return families.FirstOrDefault(name => present.Any(family => family.Name == name)) ?? families[^1];
        }
        finally
        {
            foreach (var family in present)
            {
                family.Dispose();
            }
        }
    }
}
