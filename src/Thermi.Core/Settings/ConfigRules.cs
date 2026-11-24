using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;

namespace Thermi.Core.Settings;

/// <summary>What valid settings hold beyond their shape: the ranges and combinations the file allows. Gives the first problem, naming its field.</summary>
public static partial class ConfigRules
{
    public static string? Problem(ThermiConfig config) => Problems(config).OfType<string>().FirstOrDefault();

    /// <summary>Whether <paramref name="text"/> is a color as the file writes one, #RRGGBB.</summary>
    public static bool IsColor(string text) => HexColor().IsMatch(text);

    /// <summary>The first problem of a style alone, as a style file holds it, its fields named from <paramref name="at"/>.</summary>
    internal static string? Problem(StyleConfig style, string at) => Style(style, at).OfType<string>().FirstOrDefault();

    private static IEnumerable<string?> Problems(ThermiConfig config) =>
    [
        config.Overlays.Count is >= 1 and <= ThermiConfig.MaxOverlays ? null : Say($"overlays holds {config.Overlays.Count}, not 1 to {ThermiConfig.MaxOverlays}"),
        .. (config.SensorNames ?? new Dictionary<string, string>()).Select(pair => OwnName(pair.Key, pair.Value)),
        .. config.Overlays.SelectMany((overlay, index) => Overlay(overlay, Say($"overlays[{index}]"))),
    ];

    private static IEnumerable<string?> Overlay(OverlayConfig overlay, string at) =>
    [
        Range($"{at}.size", overlay.Size, OverlayConfig.MinSize, OverlayConfig.MaxSize),
        .. Style(overlay.Style, $"{at}.style"),
        .. overlay.Items.SelectMany((item, index) => Item(item, Say($"{at}.items[{index}]"))),
    ];

    private static IEnumerable<string?> Style(StyleConfig style, string at) =>
    [
        style.Name is null or { Length: >= 1 and <= StyleConfig.MaxNameLength } ? null : Say($"{at}.name has {style.Name.Length} characters, not 1 to {StyleConfig.MaxNameLength}"),
        TextSize($"{at}.label.size", style.Label),
        TextSize($"{at}.value.size", style.Value),
        Range($"{at}.opacity.background", style.Opacity.Background, 0, 1),
        Range($"{at}.opacity.overlay", style.Opacity.Overlay, 0.2, 1),
        Range($"{at}.gap", style.Gap, 0, 16),
        Color($"{at}.colors.normal", style.Colors?.Normal),
        Color($"{at}.colors.warning", style.Colors?.Warning),
        Color($"{at}.colors.critical", style.Colors?.Critical),
        Color($"{at}.colors.label", style.Colors?.Label),
        Color($"{at}.colors.value", style.Colors?.Value),
    ];

    private static IEnumerable<string?> Item(ItemConfig item, string at) =>
    [
        new[] { item.Metric is not null, item.Sensor is not null, item.Sensors is not null }.Count(named => named) == 1 ? null : $"{at} must name one of metric, sensor or sensors",
        item.Auto is not null && item.Metric is not null ? $"{at}.auto goes only with sensor or sensors" : null,
        (item.Sensors is null) == (item.Combine is null) ? null : $"{at}.combine goes with sensors, and only with them",
        item.Smooth is 0 or (>= 2 and <= 10) ? null : Say($"{at}.smooth is {item.Smooth}, not 0 or 2 to 10"),
        item.Decimals is null or (>= 0 and <= 2) ? null : Say($"{at}.decimals is {item.Decimals}, not 0 to 2"),
        Offset($"{at}.offset", item),
        item.AlertOnly && item.Meter is null ? $"{at}.alertOnly goes only with a meter" : null,
        item.Graph && item.Meter is null ? $"{at}.graph goes only with a meter" : null,
        Color($"{at}.color", item.Color),
        .. Meter(item.Meter, $"{at}.meter"),
    ];

    private static IEnumerable<string?> Meter(MeterConfig? meter, string at) => meter is null ? [] :
    [
        (meter.Metric is null) != (meter.Sensor is null) ? null : $"{at} must name one of metric or sensor",
        meter.Auto is not null && meter.Metric is not null ? $"{at}.auto goes only with sensor" : null,
    ];

    // Without a metric, the item's type is the service's to say, so the file is held only to the widest range.
    private static string? Offset(string field, ItemConfig item)
    {
        var (min, max) = Metrics.Find(item.Metric ?? item.Auto) is { } metric ? PowerEdits.OffsetRange(metric.Kind) : PowerEdits.WidestOffset;
        return Range(field, item.Offset, min, max);
    }

    // A name of spaces alone would show as no name at all.
    private static string? OwnName(string id, string? name) =>
        name is { Length: <= SensorNames.MaxLength } && !string.IsNullOrWhiteSpace(name) ? null : Say($"sensorNames[\"{id}\"] is blank or longer than {SensorNames.MaxLength} characters");

    private static string? TextSize(string field, TextStyle? text) =>
        text is null ? null : Range(field, text.Size, 9, 20) ?? (double.IsInteger(text.Size * 2) ? null : Say($"{field} is {text.Size}, not a step of 0.5"));

    private static string? Range(string field, double value, double min, double max) =>
        value >= min && value <= max ? null : Say($"{field} is {value}, not {min} to {max}");

    private static string? Color(string field, string? color) =>
        color is null || IsColor(color) ? null : $"{field} is \"{color}\", not a color like #RRGGBB";

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HexColor();
}
