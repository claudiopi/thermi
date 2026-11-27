using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Linux.Painting;

/// <summary>A text as Pango lays it out: its width and height, and its baseline from its top, in pixels.</summary>
internal readonly record struct TextSize(double Width, double Height, double Baseline);

/// <summary>
/// The labels', values' and tags' fonts of one look at one scale, as Pango finds them, and a layout that measures and draws
/// text with them on a Cairo context. Grayscale antialiasing with unhinted metrics, so a text measures in step with the
/// scale and draws alike on a picture with no background, the screen's and the preview's.
/// </summary>
internal sealed class PainterText : IDisposable
{
    private readonly Pango.Layout _layout;
    private readonly double _scale;

    public PainterText(Cairo.Context cr, StyleFont font, TextStyle label, TextStyle value, double scale)
    {
        using var options = new Cairo.FontOptions();
        options.Antialias = Cairo.Antialias.Gray;
        options.HintMetrics = Cairo.HintMetrics.Off;
        options.HintStyle = Cairo.HintStyle.None;
        cr.SetFontOptions(options);
        _layout = PangoCairo.Functions.CreateLayout(cr);
        _scale = scale;
        Label = Describe(font, label, scale);
        Value = Describe(font, value, scale);
        Tag = Describe(font, new TextStyle(label.Size * PaintSizes.TagShare, TextWeight.Regular), scale);
    }

    /// <summary>The look whose text effect and colors the next texts are drawn in.</summary>
    public required OverlayLook Look { get; set; }

    public Pango.FontDescription Label { get; }

    public Pango.FontDescription Value { get; }

    public Pango.FontDescription Tag { get; }

    public TextSize Measure(string text, Pango.FontDescription font)
    {
        Set(text, font);
        _layout.GetExtents(out _, out var logical);
        var scale = (double)Pango.Constants.SCALE;
        return new TextSize(logical.Width / scale, logical.Height / scale, _layout.GetBaseline() / scale);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with its left middle at <paramref name="x"/>, <paramref name="y"/>, in the look's text
    /// effect, unless <paramref name="plain"/>, and gives where it ends.
    /// </summary>
    public double Draw(Cairo.Context cr, string text, Pango.FontDescription font, Rgb color, double x, double y, bool plain = false)
    {
        var (look, scale) = (Look, _scale);
        var effect = plain ? TextEffect.None : look.Effect;
        var size = Measure(text, font);
        var top = y - (size.Height / 2);
        if (effect == TextEffect.Outline)
        {
            cr.MoveTo(x, top);
            PangoCairo.Functions.LayoutPath(cr, _layout);
            cr.LineWidth = 2 * OverlayLook.OutlineWidth * scale;
            cr.LineJoin = Cairo.LineJoin.Round;
            PainterShapes.Source(cr, look.OutlineColor);
            cr.StrokePreserve();
            PainterShapes.Source(cr, color);
            cr.Fill();
            return x + size.Width;
        }

        if (effect == TextEffect.Shadow)
        {
            PainterShapes.Source(cr, look.Pills.Outer, PaintSizes.ShadowAlpha / 255.0);
            cr.MoveTo(x + scale, top + scale);
            PangoCairo.Functions.ShowLayout(cr, _layout);
        }

        PainterShapes.Source(cr, color);
        cr.MoveTo(x, top);
        PangoCairo.Functions.ShowLayout(cr, _layout);
        return x + size.Width;
    }

    public void Dispose() => _layout.Dispose();

    private void Set(string text, Pango.FontDescription font)
    {
        _layout.SetFontDescription(font);
        _layout.SetText(text, -1);
    }

    // Segoe UI, Bahnschrift and Consolas are Windows' faces: a Linux desktop has them only when someone installed them, so
    // each falls back to the faces Linux desktops ship that read alike.
    private static Pango.FontDescription Describe(StyleFont font, TextStyle text, double scale)
    {
        var families = font switch
        {
            StyleFont.Segoe => "Segoe UI Variable Text, Segoe UI, Inter, Cantarell, Noto Sans, DejaVu Sans, sans-serif",
            StyleFont.Bahnschrift => "Bahnschrift, Barlow, Roboto Condensed, Inter, Noto Sans, DejaVu Sans, sans-serif",
            StyleFont.Consolas => "Consolas, Cascadia Mono, JetBrains Mono, Ubuntu Mono, DejaVu Sans Mono, Liberation Mono, monospace",
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, "Unknown font."),
        };
        var description = Pango.FontDescription.FromString(families);
        description.SetWeight(text.Weight switch
        {
            TextWeight.Regular => Pango.Weight.Normal,
            TextWeight.Semibold => Pango.Weight.Semibold,
            TextWeight.Bold => Pango.Weight.Bold,
            _ => throw new ArgumentOutOfRangeException(nameof(text), text.Weight, "Unknown weight."),
        });
        description.SetAbsoluteSize(text.Size * scale * Pango.Constants.SCALE);
        return description;
    }
}
