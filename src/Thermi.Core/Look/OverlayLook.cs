using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>A color as red, green and blue, written #RRGGBB.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>A color the settings hold, which their rules already checked to be #RRGGBB.</summary>
    public static Rgb Parse(string hex) => new(Channel(hex, 1), Channel(hex, 3), Channel(hex, 5));

    /// <summary>The contrast ratio of the web's accessibility guidelines, from 1 for the same color to 21 for black on white.</summary>
    public double ContrastWith(Rgb other)
    {
        var (lighter, darker) = (Math.Max(Luminance, other.Luminance), Math.Min(Luminance, other.Luminance));
        return (lighter + 0.05) / (darker + 0.05);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>This color drawn at <paramref name="alpha"/>, 0 to 1, over <paramref name="under"/>.</summary>
    public Rgb Over(Rgb under, double alpha) => new(Mix(R, under.R, alpha), Mix(G, under.G, alpha), Mix(B, under.B, alpha));

    private static byte Mix(byte color, byte under, double alpha) => (byte)Math.Round((color * alpha) + (under * (1 - alpha)), MidpointRounding.AwayFromZero);

    private double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte Channel(string hex, int at) => byte.Parse(hex.AsSpan(at, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}

/// <summary>One kind of flat pill: its color inside its border, its border, a graph's unlit background, and its Auto label and value colors.</summary>
public sealed record PillColors(Rgb Outer, Rgb Border, Rgb MeterOff, Rgb Label, Rgb Value)
{
    public static PillColors Dark { get; } = new(Rgb.Parse("#20201E"), Rgb.Parse("#31312F"), Rgb.Parse("#2E2E2C"), Rgb.Parse("#A9A9A7"), Rgb.Parse("#EFEFEE"));

    public static PillColors Light { get; } = new(Rgb.Parse("#F3F3F1"), Rgb.Parse("#DADAD8"), Rgb.Parse("#D5D5D3"), Rgb.Parse("#5C5C5A"), Rgb.Parse("#1A1A1A"));
}

/// <summary>A text color as drawn. <see cref="Unreadable"/> is true when it is the Auto color drawn instead of one too close to its pill, which the window points out.</summary>
public readonly record struct TextColor(Rgb Color, bool Unreadable = false);

/// <summary>
/// Windows' high contrast colors, which the look takes over every color of the style: pills in the window color, text,
/// borders and the normal level in the window text color, warning in the highlight color and critical in the hot-track color.
/// </summary>
public sealed record HighContrastColors(Rgb Window, Rgb WindowText, Rgb Highlight, Rgb HotTrack);

/// <summary>Where one pill goes, in the overlay's pixels.</summary>
public readonly record struct PillBox(double Left, double Top, double Width, double Height);

/// <summary>Every pill's place, and the size of the whole overlay.</summary>
public sealed record Arrangement(IReadOnlyList<PillBox> Pills, double Width, double Height);

/// <summary>
/// A style made ready to draw: its preset turned into sizes and weights, its pills into their colors, every text color
/// into a readable one, and the Auto text effect into a shadow or none. The window's stage and the overlay both draw
/// from it, so they always agree.
/// </summary>
public sealed record OverlayLook
{
    /// <summary>The least contrast a chosen text color needs against its pill's color, taken as opaque.</summary>
    public const double MinContrast = 3;

    /// <summary>Below this background opacity, Auto text gets a shadow so it reads on any wallpaper.</summary>
    public const double ShadowBelow = 0.3;

    /// <summary>The outline's width in pixels at 100 %, drawn in the pill's color.</summary>
    public const double OutlineWidth = 1;

    /// <summary>How much of the value's color a neutral meter takes over its pill, so normal draws no eye.</summary>
    public const double Neutral = 0.35;

    public static Rgb DefaultNormal { get; } = Rgb.Parse("#3DC86A");

    public static Rgb DefaultWarning { get; } = Rgb.Parse("#FEC84B");

    public static Rgb DefaultCritical { get; } = Rgb.Parse("#F05A50");

    /// <summary>The default warning color on light pills, darker so it stands out from them.</summary>
    public static Rgb LightWarning { get; } = Rgb.Parse("#9D5D00");

    /// <summary>The default critical color on light pills, darker so it stands out from them.</summary>
    public static Rgb LightCritical { get; } = Rgb.Parse("#C42B1C");

    public required PillLayout Layout { get; init; }

    public required Orientation Orientation { get; init; }

    /// <summary>False when the pills show their values without labels.</summary>
    public required bool Labels { get; init; }

    /// <summary>Pixels at 100 % between pills.</summary>
    public required int Gap { get; init; }

    public required TextStyle Label { get; init; }

    public required TextStyle Value { get; init; }

    public required PillColors Pills { get; init; }

    public required TextColor LabelColor { get; init; }

    public required TextColor ValueColor { get; init; }

    public required Rgb Normal { get; init; }

    public required Rgb Warning { get; init; }

    public required Rgb Critical { get; init; }

    /// <summary>The pills' background opacity, 0 to 1.</summary>
    public required double Background { get; init; }

    /// <summary>The whole overlay's opacity, 0.2 to 1.</summary>
    public required double Opacity { get; init; }

    /// <summary>The effect the text is drawn with, never <see cref="TextEffect.Auto"/>.</summary>
    public required TextEffect Effect { get; init; }

    public required StyleFont Font { get; init; }

    public required CornerStyle Corners { get; init; }

    /// <summary>True when Windows' high contrast colors replace the style's, an item's own value color included.</summary>
    public bool HighContrast { get; init; }

    /// <summary>The outline's color: the pill's, so the text stands out from whatever is behind it.</summary>
    public Rgb OutlineColor => Pills.Outer;

    /// <summary>
    /// The look of a style on pills that follow, when they do, the mode of the taskbar the overlay sits on. Under high
    /// contrast, Windows' colors replace every color of the style.
    /// </summary>
    public static OverlayLook Of(StyleConfig style, bool lightTaskbar, HighContrastColors? highContrast = null)
    {
        var look = Styled(style, lightTaskbar);
        return highContrast is not { } system ? look : look with
        {
            Pills = new PillColors(system.Window, system.WindowText, system.Window, system.WindowText, system.WindowText),
            LabelColor = new TextColor(system.WindowText),
            ValueColor = new TextColor(system.WindowText),
            Normal = system.WindowText,
            Warning = system.Highlight,
            Critical = system.HotTrack,
            HighContrast = true,
        };
    }

    /// <summary>A pill's corner radius for its height: half of it for round, a quarter for soft, none for square.</summary>
    public double Radius(double height) => Corners switch
    {
        CornerStyle.Round => height / 2,
        CornerStyle.Soft => height / 4,
        CornerStyle.Square => 0,
        _ => 0,
    };

    private static OverlayLook Styled(StyleConfig style, bool lightTaskbar)
    {
        var light = style.Pills == PillTheme.Light || (style.Pills == PillTheme.System && lightTaskbar);
        var pills = light ? PillColors.Light : PillColors.Dark;
        var (label, value) = StylePresets.Text(style);
        var valueColor = Readable(style.Colors?.Value, pills, pills.Value);
        return new OverlayLook
        {
            Layout = style.Layout,
            Orientation = style.Orientation,
            Labels = !style.NoLabels,
            Gap = style.Gap,
            Font = style.Font,
            Corners = style.Corners,
            Label = label,
            Value = value,
            Pills = pills,
            LabelColor = Readable(style.Colors?.Label, pills, pills.Label),
            ValueColor = valueColor,
            Normal = Color(style.Colors?.Normal, valueColor.Color.Over(pills.Outer, Neutral)),
            Warning = Color(style.Colors?.Warning, light ? LightWarning : DefaultWarning),
            Critical = Color(style.Colors?.Critical, light ? LightCritical : DefaultCritical),
            Background = style.Opacity.Background,
            Opacity = style.Opacity.Overlay,
            Effect = style.Effect != TextEffect.Auto ? style.Effect : style.Opacity.Background < ShadowBelow ? TextEffect.Shadow : TextEffect.None,
        };
    }

    /// <summary>An item's value color: its own when it has a readable one and high contrast is off, else the style's.</summary>
    public TextColor ValueColorOf(OverlayItem item) => item.Color is null || HighContrast ? ValueColor : Readable(item.Color, Pills, Pills.Value);

    public Rgb MeterColor(Level level) => level switch
    {
        Level.Critical => Critical,
        Level.Warning => Warning,
        Level.Normal => Normal,
        _ => Normal,
    };

    /// <summary>
    /// Places pills of the given widths and one height, in pixels at <paramref name="scale"/>: side by side, or stacked
    /// from top to bottom and all as wide as the widest, with the style's gap between them.
    /// </summary>
    public Arrangement Arrange(IReadOnlyList<double> widths, double height, double scale)
    {
        var gap = Gap * scale;
        var across = gap * Math.Max(0, widths.Count - 1);
        if (Orientation == Orientation.Vertical)
        {
            var widest = widths.DefaultIfEmpty(0).Max();
            return new Arrangement([.. widths.Select((_, at) => new PillBox(0, at * (height + gap), widest, height))], widest, (height * widths.Count) + across);
        }

        var boxes = new List<PillBox>(widths.Count);
        var left = 0.0;
        foreach (var width in widths)
        {
            boxes.Add(new PillBox(left, 0, width, height));
            left += width + gap;
        }

        return new Arrangement(boxes, widths.Sum() + across, height);
    }

    private static Rgb Color(string? hex, Rgb fallback) => hex is null ? fallback : Rgb.Parse(hex);

    private static TextColor Readable(string? hex, PillColors pills, Rgb auto) =>
        hex is null ? new TextColor(auto)
        : Rgb.Parse(hex) is var chosen && chosen.ContrastWith(pills.Outer) >= MinContrast ? new TextColor(chosen)
        : new TextColor(auto, Unreadable: true);
}

/// <summary>The presets' text: labels semibold and values bold in all three, Standard being thermi's first look.</summary>
public static class StylePresets
{
    public static (TextStyle Label, TextStyle Value) Of(StylePreset preset) => preset switch
    {
        StylePreset.Compact => (new TextStyle(11, TextWeight.Semibold), new TextStyle(12, TextWeight.Bold)),
        StylePreset.Large => (new TextStyle(16, TextWeight.Semibold), new TextStyle(17, TextWeight.Bold)),
        StylePreset.Standard or StylePreset.Custom => (new TextStyle(13, TextWeight.Semibold), new TextStyle(13.5, TextWeight.Bold)),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown preset."),
    };

    /// <summary>The text a style shows: its preset's, or for a custom one its own, a text it leaves out taking Standard's, which custom starts from.</summary>
    public static (TextStyle Label, TextStyle Value) Text(StyleConfig style)
    {
        var (label, value) = Of(style.Preset);
        return style.Preset == StylePreset.Custom ? (style.Label ?? label, style.Value ?? value) : (label, value);
    }
}
