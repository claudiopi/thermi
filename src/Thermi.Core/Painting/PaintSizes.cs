using Thermi.Core.Look;

namespace Thermi.Core.Painting;

/// <summary>
/// The sizes every painter of the overlay draws with, in pixels at 100 %, times the scale: GDI+ on Windows and Cairo on
/// Linux lay the same pills out from them, so an overlay keeps its shape on both, and only how wide each text measures is
/// the painter's own.
/// </summary>
public static class PaintSizes
{
    public const float InnerPadding = 10;
    public const float LabelGap = 10;
    public const float MeterGap = 6;
    public const float ItemGap = 14;

    /// <summary>Between the pills a single bar holds, as wide as the bar's padding around them.</summary>
    public const float InnerPillGap = 4;

    public const float TagGap = 4;
    public const float UnderlineGap = 2;
    public const float UnderlineHeight = 2;
    public const float OuterPadding = 4;
    public const float GraphWidth = 24;
    public const float MeterWidth = 12;

    /// <summary>The width of thermi's mark on the "Sensors stopped" pill.</summary>
    public const float MarkWidth = 16;

    public const float GraphLine = 1.5f;
    public const float ReversedPadding = 3;
    public const float ReversedRadius = 3;
    public const float GlowWidth = 2;

    /// <summary>The shadow's alpha, of 255: it takes the pill's color, so it sets the text apart from the wallpaper as the pill would.</summary>
    public const int ShadowAlpha = 160;

    /// <summary>How much of its level's color a graph's fill takes, of 255: light, so the line reads first.</summary>
    public const int GraphFill = 77;

    /// <summary>How much of the critical color a glowing pill takes at the glow's height, of 255.</summary>
    public const int GlowTint = 90;

    /// <summary>A tag's size over its label's: said small and quiet after its value, so the value stays what the eye finds first.</summary>
    public const double TagShare = 0.8;

    /// <summary>In unscaled pixels: a graph's or a meter's width and its gap to the value, or nothing.</summary>
    public static float MeterRoom(OverlayItem item, OverlayLook look) =>
        (float)look.MeterStretch * (item.Graph is not null ? GraphWidth + MeterGap
        : item.Lit is not null ? MeterWidth + MeterGap
        : 0);
}
