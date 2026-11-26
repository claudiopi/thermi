using System.Text.Json.Serialization;
using Thermi.Core.Translations;

namespace Thermi.Core.Settings;

/// <summary>
/// The look of an overlay, with its <see cref="Layout"/>, <see cref="Orientation"/> and <see cref="NoLabels"/>, which
/// belong to the overlay and are no part of the look. <see cref="Name"/> is the saved style it came from, left out once anything else changes the
/// look. Unless <see cref="Preset"/> is custom, it sets <see cref="Label"/> and <see cref="Value"/>, and those are
/// ignored. <see cref="Gap"/> is in pixels at 100 %.
/// </summary>
public sealed record StyleConfig(int Gap = 8, bool Units = true)
{
    public const int MaxNameLength = 40;

    public const int MaxGap = 16;

    /// <summary>This look on an overlay whose style is <paramref name="overlay"/>: its layout and orientation stay the overlay's.</summary>
    public StyleConfig WornOn(StyleConfig overlay) => this with { Layout = overlay.Layout, Orientation = overlay.Orientation, NoLabels = overlay.NoLabels };

    public string? Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PillLayout Layout { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Orientation Orientation { get; init; }

    /// <summary>True when the pills show their values without labels, each pill keeping its items.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoLabels { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public StylePreset Preset { get; init; }

    public TextStyle? Label { get; init; }

    public TextStyle? Value { get; init; }

    public OpacityConfig Opacity { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PillTheme Pills { get; init; }

    public StyleColors? Colors { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public StyleFont Font { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CornerStyle Corners { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TextEffect Effect { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}

/// <summary>A text's size in pixels at 100 %, and its weight.</summary>
public sealed record TextStyle(double Size, TextWeight Weight)
{
    public const double MinSize = 9;

    public const double MaxSize = 36;

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}

/// <summary>The pills' background, 0 to 1, and the whole overlay's, text and meters included, 0.2 to 1 so it never vanishes.</summary>
public sealed record OpacityConfig(double Background = 1, double Overlay = 1)
{
    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}

/// <summary>
/// Colors as #RRGGBB. A meter color left out takes its default, a text color left out is Auto, and the pills' color left
/// out is the theme's.
/// </summary>
public sealed record StyleColors
{
    /// <summary>Okabe-Ito's blue, orange and vermilion, which people with any common color blindness tell apart.</summary>
    public static StyleColors ColorblindSafe { get; } = new() { Normal = "#0072B2", Warning = "#E69F00", Critical = "#D55E00" };

    public string? Normal { get; init; }

    public string? Warning { get; init; }

    public string? Critical { get; init; }

    public string? Label { get; init; }

    public string? Value { get; init; }

    /// <summary>The pills' own color, over the theme's.</summary>
    public string? Background { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}

/// <summary>Pills as runs of one label, one pill per item, or one bar for all.</summary>
[Translated]
[JsonConverter(typeof(ConfigEnum<PillLayout>))]
public enum PillLayout
{
    Pills,
    Each,
    One,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<Orientation>))]
public enum Orientation
{
    Horizontal,
    Vertical,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<StylePreset>))]
public enum StylePreset
{
    Standard,
    Compact,
    Large,
    Custom,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<TextWeight>))]
public enum TextWeight
{
    Regular,
    Semibold,
    Bold,
}

/// <summary>The mode of the taskbar the overlay sits on, the default, or dark or light pills.</summary>
[Translated]
[JsonConverter(typeof(ConfigEnum<PillTheme>))]
public enum PillTheme
{
    System,
    Dark,
    Light,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<StyleFont>))]
public enum StyleFont
{
    Segoe,
    Bahnschrift,
    Consolas,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<CornerStyle>))]
public enum CornerStyle
{
    Round,
    Soft,
    Square,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<TextEffect>))]
public enum TextEffect
{
    Auto,
    Shadow,
    Outline,
    None,
}
