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

    /// <summary>This color moved <paramref name="share"/> of the way, 0 to 1, toward <paramref name="other"/>.</summary>
    public Rgb Toward(Rgb other, double share) => new(Mix(R, other.R, share), Mix(G, other.G, share), Mix(B, other.B, share));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    private static byte Mix(byte from, byte to, double share) => (byte)Math.Round(from + ((to - from) * share));

    private double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static byte Channel(string hex, int at) => byte.Parse(hex.AsSpan(at, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
}

/// <summary>
/// One kind of flat pill: its colors inside and border, a graph's unlit background, Auto label and value, and the pills a
/// single bar holds. <see cref="IsLight"/> pills take dark text.
/// </summary>
public sealed record PillColors(Rgb Outer, Rgb Border, Rgb MeterOff, Rgb Label, Rgb Value, Rgb Inner)
{
    /// <summary>The least contrast Auto text keeps against pills of a color of their own, the web's guidelines' for text.</summary>
    public const double TextContrast = 4.5;

    // The inner pills stand 1.5:1 from the bar, lighter on dark and darker on light, so the runs read apart quietly.
    private const double InnerContrast = 1.5;

    private static readonly Rgb Black = new(0, 0, 0);

    private static readonly Rgb White = new(255, 255, 255);

    public static PillColors Dark { get; } =
        new(Rgb.Parse("#20201E"), Rgb.Parse("#31312F"), Rgb.Parse("#2E2E2C"), Rgb.Parse("#A9A9A7"), Rgb.Parse("#EFEFEE"), Rgb.Parse("#3E3E3B"));

    public static PillColors Light { get; } =
        new(Rgb.Parse("#F3F3F1"), Rgb.Parse("#DADAD8"), Rgb.Parse("#D5D5D3"), Rgb.Parse("#5C5C5A"), Rgb.Parse("#1A1A1A"), Rgb.Parse("#C8C8C6"))
        {
            IsLight = true,
        };

    public bool IsLight { get; init; }

    /// <summary>
    /// Pills of <paramref name="fill"/>, light where black stands farther from it than white, with Auto text that keeps
    /// <see cref="TextContrast"/> against it.
    /// </summary>
    public static PillColors Of(Rgb fill)
    {
        var light = fill.ContrastWith(Black) >= fill.ContrastWith(White);
        var kind = light ? Light : Dark;
        var value = kind.Value.ContrastWith(fill) >= TextContrast ? kind.Value : light ? Black : White;
        var label = kind.Label.ContrastWith(fill) >= TextContrast ? kind.Label : value;
        var inner = Enumerable.Range(1, 100)
            .Select(step => fill.Toward(value, step / 100.0))
            .FirstOrDefault(mixed => mixed.ContrastWith(fill) >= InnerContrast, value);
        return new PillColors(fill, fill.Toward(value, 0.12), fill.Toward(value, 0.08), label, value, inner) { IsLight = light };
    }
}

/// <summary>
/// A text color as drawn. <see cref="Unreadable"/> is true when it is the Auto color drawn instead of one too close to its pill,
/// which the window points out.
/// </summary>
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
    /// <summary>A pill's height in pixels at 100 %, for text up to 20 px.</summary>
    public const int PillHeight = 38;

    /// <summary>A pill's height over its larger text's size, for text over 20 px, so no text leaves its pill.</summary>
    public const double TextRoom = 1.9;

    /// <summary>
    /// The least contrast a chosen text color needs against its pill's color, taken as opaque, and a default level color
    /// against it and the unlit track.
    /// </summary>
    public const double MinContrast = 3;

    /// <summary>Below this background opacity, Auto text gets a shadow so it reads on any wallpaper.</summary>
    public const double ShadowBelow = 0.3;

    /// <summary>The outline's width in pixels at 100 %, drawn in the pill's color.</summary>
    public const double OutlineWidth = 1;

    /// <summary>
    /// The default normal color, Windows' success green, as CleanMeter's meters read below their limit; it keeps 3:1
    /// against dark pills and their graphs' unlit track.
    /// </summary>
    public static Rgb DefaultNormal { get; } = Rgb.Parse("#6CCB5F");

    /// <summary>The default normal color on light pills, Windows' darker success green, so it stands out from them.</summary>
    public static Rgb LightNormal { get; } = Rgb.Parse("#0F7B0F");

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

    /// <summary>A pill's height in pixels at 100 %, as <see cref="HeightOf"/> gives it.</summary>
    public double Height { get; init; } = PillHeight;

    /// <summary>
    /// How much wider meters and graphs are drawn, with their gap to the value, than beside Standard's values: as much as
    /// larger values are, so they keep their shape, and never narrower.
    /// </summary>
    public double MeterStretch => Math.Max(1, Value.Size / StylePresets.Of(StylePreset.Standard).Value.Size);

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
            Pills = new PillColors(system.Window, system.WindowText, system.Window, system.WindowText, system.WindowText, system.Window),
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

    /// <summary>The inner pill's corner radius: the bar's less the padding, so both curves share a center and the band stays even.</summary>
    public double InnerRadius(double barHeight, double padding) => Math.Max(0, Radius(barHeight) - padding);

    /// <summary>A pill's height in pixels at 100 % for a style's text.</summary>
    public static double HeightOf(StyleConfig style)
    {
        var (label, value) = StylePresets.Text(style);
        return Math.Max(PillHeight, TextRoom * Math.Max(label.Size, value.Size));
    }

    // Below ShadowBelow what shows behind the text is what is behind the overlay, so Light, Dark or own-colored pills there
    // take the taskbar's mode, as System's do, and their text, unlit segments and shadow read on it.
    private static OverlayLook Styled(StyleConfig style, bool lightTaskbar)
    {
        var faint = style.Opacity.Background < ShadowBelow;
        var light = faint ? lightTaskbar : style.Pills == PillTheme.Light || (style.Pills == PillTheme.System && lightTaskbar);
        var themed = light ? PillColors.Light : PillColors.Dark;
        var pills = !faint && style.Colors?.Background is { } own ? PillColors.Of(Rgb.Parse(own)) : themed;
        light = pills.IsLight;
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
            Height = HeightOf(style),
            Pills = pills,
            LabelColor = Readable(style.Colors?.Label, pills, pills.Label),
            ValueColor = valueColor,
            Normal = Color(style.Colors?.Normal, Kept(light ? LightNormal : DefaultNormal, pills)),
            Warning = Color(style.Colors?.Warning, Kept(light ? LightWarning : DefaultWarning, pills)),
            Critical = Color(style.Colors?.Critical, Kept(light ? LightCritical : DefaultCritical, pills)),
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
    /// from top to bottom and all as wide as the widest, with the style's gap between them. Stacked, a pill of
    /// <paramref name="heights"/> takes its own, as a single bar standing with its pills inside it.
    /// </summary>
    public Arrangement Arrange(IReadOnlyList<double> widths, double height, double scale, IReadOnlyList<double>? heights = null)
    {
        var gap = Gap * scale;
        var across = gap * Math.Max(0, widths.Count - 1);
        if (Orientation == Orientation.Vertical)
        {
            var widest = widths.DefaultIfEmpty(0).Max();
            var tall = widths.Select((_, at) => heights?[at] ?? height).ToList();
            return new Arrangement(
                [.. tall.Select((each, at) => new PillBox(0, tall.Take(at).Sum() + (at * gap), widest, each))],
                widest,
                tall.Sum() + across);
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

    // On pills of a color of their own, a default level color may fall under 3:1 against them or the unlit track.
    private static Rgb Kept(Rgb level, PillColors pills) =>
        Enumerable.Range(0, 21)
            .Select(step => level.Toward(pills.Value, step * 0.05))
            .FirstOrDefault(moved => Math.Min(moved.ContrastWith(pills.Outer), moved.ContrastWith(pills.MeterOff)) >= MinContrast, pills.Value);

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
