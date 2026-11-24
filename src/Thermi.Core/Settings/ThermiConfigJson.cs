using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Look;

namespace Thermi.Core.Settings;

/// <summary>
/// Reads and writes the settings file. Reading never throws: a broken file, or one from a newer thermi,
/// gives the defaults and says why. A version 1 file is migrated, and every read gives each item its id.
/// </summary>
public static class ThermiConfigJson
{
    // People edit this file by hand: hotkeys like Ctrl+Alt+T and labels in any language stay readable.
    // It is never put in a web page, where those characters would need escaping.
    private static readonly ConfigJson Context =
        new(new JsonSerializerOptions(ConfigJson.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static string Write(ThermiConfig config) => JsonSerializer.Serialize(config, Context.ThermiConfig);

    public static (ThermiConfig Config, ConfigProblem? Problem) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (new ThermiConfig(), null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException ex)
        {
            return (new ThermiConfig(), ConfigProblem.Broken(ex));
        }
        catch (ArgumentException ex)
        {
            return Broken(ex.Message);
        }
    }

    /// <summary>The version a file says it is, 1 when it says none, or null for text that is not a JSON object.</summary>
    public static int? VersionOf(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "");
            return document.RootElement.ValueKind == JsonValueKind.Object ? VersionOf(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the text is not yet a whole file: empty, or JSON that ends before its root closes, as while an editor
    /// saves. Text broken before its end is whole, and broken.
    /// </summary>
    public static bool IsUnfinished(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), isFinalBlock: false, state: default);
        try
        {
            while (reader.Read())
            {
                if (reader.CurrentDepth == 0 && reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                {
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return true;
    }

    private static (ThermiConfig Config, ConfigProblem? Problem) Read(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Null)
        {
            return (new ThermiConfig(), null);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Broken("it is not a JSON object");
        }

        var version = VersionOf(root);
        if (version > ThermiConfig.CurrentVersion)
        {
            return (new ThermiConfig(), ConfigProblem.Newer(version));
        }

        var read = version switch
        {
            1 => ConfigMigration.FromVersion3(ConfigMigration.FromVersion1(root.Deserialize(Version1Json.Default.Version1Config))),
            2 => ConfigMigration.FromVersion3(ConfigMigration.FromVersion2(Complete(root.Deserialize(Context.ThermiConfig)))),
            3 => ConfigMigration.FromVersion3(Complete(root.Deserialize(Context.ThermiConfig))),
            ThermiConfig.CurrentVersion => Complete(root.Deserialize(Context.ThermiConfig)),
            _ => null,
        };
        if (read is null)
        {
            return Broken($"version {version} is not one thermi knows");
        }

        var config = ItemIds.Assign(read);
        return ConfigRules.Problem(config) is { } problem ? Broken(problem) : (config, null);
    }

    // Every file thermi wrote has a version, so one without it was written by hand for version 1.
    private static int VersionOf(JsonElement root) =>
        !root.TryGetProperty("version", out var version) ? 1
        : version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number
        : throw new JsonException("The version is not a whole number.");

    private static (ThermiConfig Config, ConfigProblem? Problem) Broken(string reason) => (new ThermiConfig(), ConfigProblem.Broken(reason));

    // The generated reader leaves a list or object the file leaves out as null, and people write nulls by
    // hand: every missing piece gets its default here, and an item that names no sensor is left out.
    private static ThermiConfig Complete(ThermiConfig? read) => read is null ? new ThermiConfig() : read with
    {
        Version = ThermiConfig.CurrentVersion,
        Overlays = read.Overlays is null ? new ThermiConfig().Overlays : [.. read.Overlays.OfType<OverlayConfig>().Select(Complete)],
    };

    private static OverlayConfig Complete(OverlayConfig read) => read with
    {
        Style = StyleFile.Complete(read.Style),
        Items = read.Items is null ? OverlayConfig.DefaultItems : [.. read.Items.OfType<ItemConfig>().Select(Complete).Where(NamesASensor)],
    };

    private static ItemConfig Complete(ItemConfig read) => read with
    {
        Sensors = read.Sensors?.OfType<string>().ToList() is { Count: > 0 } sensors ? sensors : null,
    };

    private static bool NamesASensor(ItemConfig item) => item.Metric is not null || item.Sensor is not null || item.Sensors is not null;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ThermiConfig))]
internal sealed partial class ConfigJson : JsonSerializerContext;
