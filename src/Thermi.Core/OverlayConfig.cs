using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Core;

/// <summary>Where the overlay sits, in physical pixels of the whole desktop (all monitors).</summary>
public sealed record Position(int X, int Y);

/// <summary>
/// One entry in the overlay: a metric's value, an optional meter driven by another metric (the fan shows
/// its speed and meters its duty), and the threshold that colors the meter.
/// </summary>
public sealed record ItemConfig(string Metric, string? Meter = null, Threshold? Threshold = null);

/// <summary>The user's overlay settings. Every change to its shape gets a new <see cref="Version"/> and a migration.</summary>
public sealed record OverlayConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Null until the user places the overlay: then it sits at the right end of the main taskbar.</summary>
    public Position? Position { get; init; }

    public IReadOnlyList<ItemConfig> Items { get; init; } =
    [
        new(Metrics.CpuFan.Id, Meter: Metrics.CpuFanDuty.Id, Threshold: new Threshold(60, 85)),
        new(Metrics.CpuTemperature.Id, Meter: Metrics.CpuTemperature.Id, Threshold: new Threshold(70, 85)),
        new(Metrics.CpuLoad.Id, Meter: Metrics.CpuLoad.Id, Threshold: new Threshold(60, 80)),
        new(Metrics.CpuPower.Id),
        new(Metrics.GpuLoad.Id, Meter: Metrics.GpuLoad.Id, Threshold: new Threshold(60, 80)),
        new(Metrics.RamUsed.Id, Meter: Metrics.RamLoad.Id, Threshold: new Threshold(60, 80)),
    ];

    /// <summary>Metric id to source sensor id, for when the automatic choice picks the wrong sensor.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Reads and writes the config file. Reading never throws: a broken file gives the defaults and says why.</summary>
public static class OverlayConfigJson
{
    public static string Write(OverlayConfig config) => JsonSerializer.Serialize(config, ConfigJson.Default.OverlayConfig);

    public static (OverlayConfig Config, string? Problem) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (new OverlayConfig(), null);
        }

        try
        {
            var config = JsonSerializer.Deserialize(json, ConfigJson.Default.OverlayConfig) ?? new OverlayConfig();
            return config.Version > OverlayConfig.CurrentVersion
                ? (new OverlayConfig(), $"The config was written by a newer thermi (version {config.Version}); using the defaults.")
                : (Complete(config), null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return (new OverlayConfig(), $"The config could not be read ({ex.Message}); using the defaults.");
        }
    }

    // The generated reader leaves a setting the file leaves out as null, not as its default, and people
    // write nulls by hand: every missing piece gets its default here.
    private static OverlayConfig Complete(OverlayConfig read)
    {
        var defaults = new OverlayConfig();
        return read with
        {
            Version = OverlayConfig.CurrentVersion,
            Items = read.Items is null ? defaults.Items : [.. read.Items.Where(item => item?.Metric is not null)],
            Sources = read.Sources ?? defaults.Sources,
        };
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OverlayConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
