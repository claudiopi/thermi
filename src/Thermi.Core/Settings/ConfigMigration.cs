using System.Globalization;
using System.Text.Json.Serialization;
using Thermi.Core.Look;
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
            Meter = item.Meter is null ? null : new MeterConfig { Metric = item.Meter },
            Threshold = calm && Metrics.Find(item.Meter) is { } metered ? Threshold.Default(metered) : item.Threshold,
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
            Items = [.. overlay.Items.Select(item => OverlayConfig.GraphsByDefault(item) && item is { Threshold: null, AlertOnly: false }
                ? item with { Graph = true }
                : item)],
        })],
    };

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
        var meter = Metrics.Find(item.Meter?.Metric ?? item.Meter?.Auto);
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
        return named.Meter?.Metric is { } meter ? named with { Meter = NamedMeter(meter, sources) } : named;
    }

    private static MeterConfig NamedMeter(string metric, Dictionary<string, string> sources)
    {
        if (sources.TryGetValue(metric, out var sensor))
        {
            return new MeterConfig { Sensor = sensor, Auto = metric };
        }

        return metric == Metrics.CpuFanDuty.Id && sources.TryGetValue(Metrics.CpuFan.Id, out var fan) && SensorMap.OwnDuty(fan) is { } duty
            ? new MeterConfig { Sensor = duty, Auto = metric }
            : new MeterConfig { Metric = metric };
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Version1Config))]
internal sealed partial class Version1Json : JsonSerializerContext;
