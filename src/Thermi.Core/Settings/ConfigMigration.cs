using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Core.Settings;

/// <summary>
/// Version 1, as thermi 0.1 wrote it: one overlay, its items by metric, and sources that named the sensor
/// behind a metric for every item and meter at once.
/// </summary>
internal sealed record Version1Config(Position? Position, IReadOnlyList<Version1Item?>? Items, IReadOnlyDictionary<string, string>? Sources);

internal sealed record Version1Item(string? Metric, string? Meter, Threshold? Threshold);

/// <summary>
/// Turns a version 1 file into one overlay of version 2 that shows the same. Each source goes to the items
/// and meters that show its metric, which they keep as auto; a fan duty meter on a named fan becomes that
/// fan's own duty. A source goes over as it is, even of the wrong type, so it still shows as missing. An item just as
/// thermi 0.1 made it by default becomes this version's default of its metric, since 0.1 wrote its defaults to the file
/// the first time it saved anything, so nobody chose them, unless a source names a sensor for its meter; lines equal to
/// 0.1's defaults for their metric take this version's defaults of their meter, so only lines the user moved stay. Items
/// and the overlay keep no name, so they are named in the window's language.
/// <para>
/// A version 2 file, which earlier builds of this version wrote, becomes version 3: what those builds wrote without the
/// user choosing it takes what this one gives. Lines whose critical line is still 0.1's default for their meter take this
/// version's, so a warning nudged on a meter that now starts without lines, as RAM's, goes with them; a name
/// an overlay or a metric's item was given as thermi named it then, in any language, is dropped, so it is named in the
/// window's language; and a custom look's labels at the 10 px those builds gave them take Standard's, the look itself
/// Standard when its values are Standard's too. Lines and names the user typed keep, unless they read as thermi's own.
/// </para>
/// </summary>
internal static class ConfigMigration
{
    // The label size earlier builds of version 2 gave a custom look.
    private const double Version2LabelSize = 10;

    // The file keeps lines to a tenth.
    private const double Tenth = 0.1;

    private static readonly Version1Item[] Version1Defaults =
    [
        new(Metrics.CpuFan.Id, Metrics.CpuFanDuty.Id, new Threshold(60, 85)),
        new(Metrics.CpuTemperature.Id, Metrics.CpuTemperature.Id, new Threshold(70, 85)),
        new(Metrics.CpuLoad.Id, Metrics.CpuLoad.Id, new Threshold(60, 80)),
        new(Metrics.CpuPower.Id, null, null),
        new(Metrics.GpuLoad.Id, Metrics.GpuLoad.Id, new Threshold(60, 80)),
        new(Metrics.RamUsed.Id, Metrics.RamLoad.Id, new Threshold(60, 80)),
    ];

    public static ThermiConfig FromVersion1(Version1Config? file)
    {
        // A source written by hand as null names no sensor, so its metric is picked as if it were left out.
        var sources = (file?.Sources ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .Where(source => source.Value is not null)
            .ToDictionary(StringComparer.Ordinal);
        var items = file?.Items is { } listed
            ? listed.OfType<Version1Item>().Where(item => item.Metric is not null).Select(item => ItemOf(item, sources))
            : OverlayConfig.DefaultItems;
        var overlay = new OverlayConfig
        {
            Position = file?.Position,
            Items = [.. items.Select(item => Named(item, sources))],
        };
        return new ThermiConfig { Overlays = [overlay] };
    }

    private static ItemConfig ItemOf(Version1Item item, Dictionary<string, string> sources)
    {
        // Lines just as 0.1's defaults for the metric were never chosen either, so they take this version's calm ones.
        var calm = Array.Find(Version1Defaults, made => made.Metric == item.Metric) is { } old && item.Threshold == old.Threshold;
        if (Array.IndexOf(Version1Defaults, item) >= 0
            && (item.Meter is null || !sources.ContainsKey(item.Meter))
            && OverlayConfig.DefaultItems.FirstOrDefault(made => made.Metric == item.Metric) is { } made)
        {
            return made;
        }

        return new ItemConfig
        {
            Metric = item.Metric,
            WarnBy = item.Meter is null ? null : new WarnByConfig { Metric = item.Meter },
            Threshold = calm && Metrics.Find(item.Meter) is { } metered ? Threshold.Default(metered) : item.Threshold,
            Graph = item.Meter is null ? GraphStyle.None : GraphStyle.Bars,
        };
    }

    public static ThermiConfig FromVersion2(ThermiConfig config) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with
        {
            Name = overlay.Name is { } name && OverlayNamesThermiGave.Contains(Unnumbered(name)) ? null : overlay.Name,
            Style = StyleOf(overlay.Style),
            Items = [.. overlay.Items.Select(Version2ItemOf)],
        })],
    };

    /// <summary>
    /// A load metered by itself with no lines, as thermi made it, shows its graph, as a new one does; one whose look was
    /// changed keeps it.
    /// </summary>
    public static ThermiConfig FromVersion3(ThermiConfig config) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with
        {
            Items = [.. overlay.Items.Select(item =>
                item is { Graph: GraphStyle.Bars, WarnBy: { } meter, Threshold: null, AlertOnly: false }
                && Threshold.IsOwn(item, meter)
                && Metrics.Find(item.Metric ?? item.Auto)?.Kind == SensorKind.Load
                    ? item with { Graph = GraphStyle.Line }
                    : item)],
        })],
    };

    /// <summary>
    /// What an item of an older file drew and judged becomes this version's Graph and Warn by, so it shows as it did.
    /// Until here, its meter is read as what it warns by and what it drew as its graph, a meter of the item itself
    /// becomes its own reading, and an item without one draws nothing and has no lines, alert-only or notice, as it had
    /// none; what it drew is kept only where the overlay's Auto would draw something else. An item with no graph is
    /// already this version's, as the defaults version 1 takes.
    /// </summary>
    public static ThermiConfig FromVersion5(ThermiConfig config) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with { Items = [.. overlay.Items.Select(Version5ItemOf)] })],
    };

    /// <summary>
    /// Whether a file of versions 2 to 5 holds what <see cref="FromVersion4"/> or <see cref="Version5Shape"/> rewrite: one of
    /// the taskbar's three places, or an item.
    /// </summary>
    public static bool Reshapes(JsonElement root) =>
        root.TryGetProperty("overlays", out var overlays) && overlays.ValueKind == JsonValueKind.Array
        && overlays.EnumerateArray().Any(overlay => overlay.ValueKind == JsonValueKind.Object && (NamesTaskbarPlace(overlay) || HoldsItems(overlay)));

    private static bool NamesTaskbarPlace(JsonElement overlay) =>
        overlay.TryGetProperty("snap", out var snap) && snap.ValueKind == JsonValueKind.Object
        && snap.TryGetProperty("spot", out var spot) && spot.ValueKind == JsonValueKind.String
        && spot.GetString()!.StartsWith("taskbar-", StringComparison.Ordinal);

    private static bool HoldsItems(JsonElement overlay) =>
        overlay.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0;

    /// <summary>
    /// Reads a file of versions 2 to 5 as <see cref="FromVersion5"/> takes it, before the file is read: each item's meter
    /// as what it warns by, and what it drew, nothing without a meter, bars, or the graph its switch turned on, as its graph.
    /// </summary>
    public static JsonNode? Version5Shape(JsonNode? root)
    {
        var items = (root?["overlays"] as JsonArray ?? []).SelectMany(overlay => (overlay as JsonObject)?["items"] as JsonArray ?? []);
        foreach (var item in items.OfType<JsonObject>())
        {
            var meter = item["meter"];
            var graph = item["graph"] is JsonValue on && on.GetValueKind() == JsonValueKind.True;
            item.Remove("meter");
            item.Remove("graph");
            if (meter is not null)
            {
                item["warnBy"] = meter;
            }

            item["graph"] = meter is null ? "none" : graph ? "line" : "bars";
        }

        return root;
    }

    private static ItemConfig Version5ItemOf(ItemConfig item)
    {
        if (item.Graph is not { } drawn)
        {
            return item;
        }

        var shaped = item with { WarnBy = item.WarnBy is { } meter && Threshold.IsOwn(item, meter) ? null : item.WarnBy, Graph = null };
        if (drawn == GraphStyle.None)
        {
            var bare = shaped with { WarnBy = null, Threshold = null, AlertOnly = false, Notify = false };
            return Threshold.Judges(bare) ? bare with { Graph = GraphStyle.None } : bare;
        }

        return OverlayLayout.DrawnOf(new OverlayConfig(), shaped) == drawn ? shaped : shaped with { Graph = drawn };
    }

    /// <summary>
    /// The taskbar's three places of versions 2 to 4 become the one free taskbar place, before the file is read. Its right
    /// place stays beside the notification area, where an overlay that was never moved sits; its left one keeps the margin
    /// it sat in from the taskbar's start; and its center, which the app buttons around it decided, goes beside the
    /// notification area too, since those buttons no longer count.
    /// </summary>
    public static JsonNode? FromVersion4(JsonNode? root)
    {
        foreach (var snap in (root?["overlays"] as JsonArray ?? []).Select(overlay => (overlay as JsonObject)?["snap"]).OfType<JsonObject>())
        {
            if (snap["spot"] is JsonValue spot && spot.TryGetValue<string>(out var place) && place.StartsWith("taskbar-", StringComparison.Ordinal))
            {
                snap["spot"] = "taskbar";
                if (place == "taskbar-left")
                {
                    snap["offset"] = Snaps.Margin;
                }
            }
        }

        return root;
    }

    // Every name earlier builds gave an overlay from where it sat, in every language thermi ships.
    private static readonly HashSet<string> OverlayNamesThermiGave = [.. Texts.Languages.Select(Texts.Of).SelectMany(texts => new[]
    {
        texts.Of(NameText.Taskbar), texts.Of(NameText.Floating), texts.Of(NameText.TopLeft), texts.Of(NameText.TopRight),
        texts.Of(NameText.BottomLeft), texts.Of(NameText.BottomRight),
    }.Concat(Enum.GetValues<SnapSpot>().Select(spot => texts.Of(spot))))];

    // A name with a number after it, as a second overlay on the taskbar was named, reads as the name alone.
    private static string Unnumbered(string name) =>
        name.LastIndexOf(' ') is var space and > 0 && int.TryParse(name.AsSpan(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? name[..space]
            : name;

    private static ItemConfig Version2ItemOf(ItemConfig item)
    {
        var metric = Metrics.Find(item.Metric ?? item.Auto);
        var meter = Metrics.Find(item.WarnBy?.Metric ?? item.WarnBy?.Auto);
        var calm = meter is not null && item.Threshold is { } lines
            && Array.Exists(
                Version1Defaults,
                made => made.Meter == meter.Id
                    && made.Threshold is { Critical: { } critical }
                    && lines.Critical is { } kept
                    && Math.Abs(kept - critical) < Tenth / 2);
        var given = metric is not null && item.Sensors is null && item.Name is { } name
            && Texts.Languages.Any(language => Texts.Of(language).Of(metric.Name) == name);
        return item with
        {
            Name = given ? null : item.Name,
            Threshold = calm ? Threshold.Default(meter!) : item.Threshold,
        };
    }

    private static StyleConfig StyleOf(StyleConfig style)
    {
        if (style is not { Preset: StylePreset.Custom, Label.Size: Version2LabelSize })
        {
            return style;
        }

        var (label, value) = StylePresets.Of(StylePreset.Standard);
        return (style.Value ?? value) == value
            ? style with { Preset = StylePreset.Standard, Label = null, Value = null }
            : style with { Label = label };
    }

    private static ItemConfig Named(ItemConfig item, Dictionary<string, string> sources)
    {
        var named = item.Metric is { } metric && sources.TryGetValue(metric, out var sensor)
            ? item with { Metric = null, Sensor = sensor, Auto = metric }
            : item;
        return named.WarnBy?.Metric is { } meter ? named with { WarnBy = NamedMeter(meter, sources) } : named;
    }

    private static WarnByConfig NamedMeter(string metric, Dictionary<string, string> sources)
    {
        if (sources.TryGetValue(metric, out var sensor))
        {
            return new WarnByConfig { Sensor = sensor, Auto = metric };
        }

        return metric == Metrics.CpuFanDuty.Id && sources.TryGetValue(Metrics.CpuFan.Id, out var fan) && SensorMap.OwnDuty(fan) is { } duty
            ? new WarnByConfig { Sensor = duty, Auto = metric }
            : new WarnByConfig { Metric = metric };
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Version1Config))]
internal sealed partial class Version1Json : JsonSerializerContext;
