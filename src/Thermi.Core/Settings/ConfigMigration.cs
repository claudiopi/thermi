using System.Text.Json.Serialization;
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
/// are named in English, as the defaults are; the overlay keeps no name, so it is named from where it sits.
/// </summary>
internal static class ConfigMigration
{
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
        if (Array.IndexOf(Version1Defaults, item) >= 0 && (item.Meter is null || !sources.ContainsKey(item.Meter)) && OverlayConfig.DefaultItems.FirstOrDefault(made => made.Metric == item.Metric) is { } made)
        {
            return made;
        }

        return new ItemConfig
        {
            Name = Metrics.Find(item.Metric) is { } metric ? Texts.English.Of(metric.Name) : null,
            Metric = item.Metric,
            Meter = item.Meter is null ? null : new MeterConfig { Metric = item.Meter },
            Threshold = calm && Metrics.Find(item.Meter) is { } metered ? Threshold.Default(metered) : item.Threshold,
        };
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
