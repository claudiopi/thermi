using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Core.Settings;

// The generated reader sets an init property the file leaves out to its type's default, but keeps a
// constructor parameter's own default: so a field whose default is not its type's is a parameter.
// A flag or choice at its default is left out of the file, so a hand edit sees only what was chosen.

/// <summary>
/// The settings file: thermi's own fields, and its overlays. Every change to its shape gets a new
/// <see cref="Version"/> and a migration, and every item id is unique in the whole file.
/// </summary>
public sealed record ThermiConfig
{
    public const int CurrentVersion = 6;

    public const int MaxOverlays = 4;

    public int Version { get; init; } = CurrentVersion;

    public HotkeysConfig? Hotkeys { get; init; }

    /// <summary>A language code, or null to follow Windows.</summary>
    public string? Language { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TemperatureUnit Temperature { get; init; }

    /// <summary>The settings window's own light or dark mode, which leaves the overlay's look alone.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WindowTheme WindowTheme { get; init; }

    /// <summary>Whether AI agents may change the settings through thermi-cli or only read them, which only a person turns back.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AgentAccess Agents { get; init; }

    /// <summary>The names the user gave sensors, by the id an item names each by; null while none has one.</summary>
    public IReadOnlyDictionary<string, string>? SensorNames { get; init; }

    /// <summary>The settings thermi starts with where it finds no file: one overlay, <see cref="OverlayConfig.First"/>.</summary>
    public static ThermiConfig New { get; } = new() { Overlays = [OverlayConfig.First] };

    public IReadOnlyList<OverlayConfig> Overlays { get; init; } = [new OverlayConfig()];

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}


/// <summary>One overlay: its items, where it sits, its look, and when it shows. <see cref="Size"/> is a factor over the monitor's scale.</summary>
public sealed record OverlayConfig(double Size = 1)
{
    public const double MinSize = 0.75;

    public const double MaxSize = 4;

    // Left unnamed, so each takes its metric's name in the window's language.
    public static IReadOnlyList<ItemConfig> DefaultItems { get; } =
    [
        Default(Metrics.CpuTemperature),
        Default(Metrics.CpuFan),
        Default(Metrics.CpuLoad),
        Default(Metrics.CpuPower),
        Default(Metrics.GpuLoad),
        Default(Metrics.RamUsed, Metrics.RamLoad),
    ];

    /// <summary>
    /// The one overlay of a new settings file: the default items and FPS, which takes no room until an app in front
    /// presents frames, so a game shows it with nothing set up and the desktop shows the same overlay as without it.
    /// </summary>
    public static OverlayConfig First { get; } = new() { Items = [.. DefaultItems, Default(Metrics.Fps)] };

    /// <summary>The name the user typed, or null: then it is named from where it sits, in the window's language.</summary>
    public string? Name { get; init; }

    /// <summary>Wins over <see cref="Position"/> when the file has both.</summary>
    public SnapConfig? Snap { get; init; }

    /// <summary>A free position, in physical pixels of the whole desktop (all monitors).</summary>
    public Position? Position { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Visibility Visibility { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ClickThrough { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Locked { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Oled { get; init; }

    /// <summary>True when the overlay fades as the pointer comes near it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Fade { get; init; }

    /// <summary>True while it is hidden on its own, which is saved, unlike the "Hide overlays" flag.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hidden { get; init; }

    /// <summary>What its items draw beside their values, unless an item chooses its own; no look carries it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public GraphStyle Graph { get; init; }

    public StyleConfig Style { get; init; } = new();

    public IReadOnlyList<ItemConfig> Items { get; init; } = DefaultItems;

    /// <summary>
    /// Where the overlay is snapped, or null when it sits at its free <see cref="Position"/>.
    /// With neither, it sits on the primary monitor's taskbar, beside its notification area.
    /// </summary>
    [JsonIgnore]
    public SnapConfig? Snapped => Snap ?? (Position is null ? new SnapConfig(SnapSpot.Taskbar) : null);

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }

    private static ItemConfig Default(Metric metric, Metric? warnBy = null) => new()
    {
        Id = metric.Id,
        Metric = metric.Id,
        WarnBy = warnBy is null ? null : new WarnByConfig { Metric = warnBy.Id },
        Threshold = Threshold.Default(warnBy ?? metric),
    };
}

/// <summary>
/// One entry in an overlay. It shows a <see cref="Metric"/> (its sensor picked per machine), a
/// <see cref="Sensor"/>, or several <see cref="Sensors"/> of one type joined by <see cref="Combine"/>. An item
/// made from a metric keeps it as <see cref="Auto"/> when it names a sensor, so it can go back.
/// </summary>
public sealed record ItemConfig
{
    public string Id { get; init; } = "";

    /// <summary>What the window calls the item, given when it is added; null in a file written by hand without one.</summary>
    public string? Name { get; init; }

    public string? Metric { get; init; }

    public string? Sensor { get; init; }

    public IReadOnlyList<string>? Sensors { get; init; }

    public Combine? Combine { get; init; }

    public string? Auto { get; init; }

    /// <summary>The label of the run of items it belongs to, or null for its metric's.</summary>
    public string? Label { get; init; }

    /// <summary>True when the item starts a pill of its own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Own { get; init; }

    /// <summary>True when the item also shows as a tray icon of its own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Tray { get; init; }

    /// <summary>The value's own color as #RRGGBB, over the style's.</summary>
    public string? Color { get; init; }

    /// <summary>Another reading of the item that its lines judge and its graph draws, or null for its own.</summary>
    public WarnByConfig? WarnBy { get; init; }

    public Threshold? Threshold { get; init; }

    /// <summary>Added to the value, in its unit; a temperature's in °C.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Offset { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ItemStat Stat { get; init; }

    /// <summary>Seconds to average the value over, 0 for off.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Smooth { get; init; }

    /// <summary>Decimals to show, or null for its type's own.</summary>
    public int? Decimals { get; init; }

    /// <summary>True when a throughput shows in bits per second; its lines stay in bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bits { get; init; }

    /// <summary>What it draws beside its value, or null for what its overlay's <see cref="OverlayConfig.Graph"/> says; never Auto.</summary>
    public GraphStyle? Graph { get; init; }

    /// <summary>True when the item shows only while its lines say warning or critical.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AlertOnly { get; init; }

    /// <summary>True when Windows tells once the item has been critical for a while.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Notify { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}


/// <summary>A reading an item warns by: a <see cref="Metric"/>, or a <see cref="Sensor"/> that keeps the metric it came from as <see cref="Auto"/>.</summary>
public sealed record WarnByConfig
{
    public string? Metric { get; init; }

    public string? Sensor { get; init; }

    public string? Auto { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}

/// <summary>
/// A spot on a monitor, which is left out for the primary one. On the taskbar, <see cref="Offset"/> is how far its left
/// edge sits from the taskbar's start, in physical pixels; without one, it sits beside the notification area.
/// </summary>
public sealed record SnapConfig(SnapSpot Spot, string? Monitor = null)
{
    public int? Offset { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}


public sealed record Position(int X, int Y);

/// <summary>Each combination like "Ctrl+Alt+T", or null for off.</summary>
public sealed record HotkeysConfig
{
    public string? Toggle { get; init; }

    public string? NextStyle { get; init; }

    public string? ClickThrough { get; init; }

    public string? ResetMinMax { get; init; }

    /// <summary>What a later version added here, written back as it came.</summary>
    [JsonExtensionData, JsonInclude]
    public UnknownFields? Unknown { get; internal set; }
}


[JsonConverter(typeof(ConfigEnum<TemperatureUnit>))]
public enum TemperatureUnit
{
    [JsonStringEnumMemberName("c")]
    Celsius,

    [JsonStringEnumMemberName("f")]
    Fahrenheit,
}

/// <summary>The settings window's mode: Windows' own for apps, the default, or light or dark whatever Windows' is.</summary>
[Translated]
[JsonConverter(typeof(ConfigEnum<WindowTheme>))]
public enum WindowTheme
{
    System,
    Light,
    Dark,
}

/// <summary>What AI agents may do with the settings: change them, the default, or only read them.</summary>
[Translated]
[JsonConverter(typeof(ConfigEnum<AgentAccess>))]
public enum AgentAccess
{
    Change,
    Read,
}

public static class WindowThemes
{
    /// <summary>
    /// Whether the window is dark: as chosen, or as Windows' apps are for <see cref="WindowTheme.System"/> and under high
    /// contrast, whose colors win over any choice.
    /// </summary>
    public static bool Dark(WindowTheme chosen, bool lightApps, bool highContrast) =>
        chosen == WindowTheme.System || highContrast ? !lightApps : chosen == WindowTheme.Dark;
}

[Translated]
[JsonConverter(typeof(ConfigEnum<Visibility>))]
public enum Visibility
{
    Always,
    HideInFullscreen,
    OnlyInFullscreen,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<SnapSpot>))]
public enum SnapSpot
{
    Taskbar,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<Combine>))]
public enum Combine
{
    Max,
    Average,
    Sum,
}

/// <summary>
/// What an item draws beside its value: nothing, CleanMeter's segmented bars, or a line of its last minute; for an
/// overlay, Auto draws a line for a load judged by its own reading and bars for the rest.
/// </summary>
[Translated]
[JsonConverter(typeof(ConfigEnum<GraphStyle>))]
public enum GraphStyle
{
    Auto,
    None,
    Bars,
    Line,
}

[Translated]
[JsonConverter(typeof(ConfigEnum<ItemStat>))]
public enum ItemStat
{
    Now,
    Min,
    Max,
    Average,
}

/// <summary>Choices are written as words, never numbers, and a word thermi does not know breaks the file instead of meaning something else.</summary>
internal sealed class ConfigEnum<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false)
    where T : struct, Enum;
