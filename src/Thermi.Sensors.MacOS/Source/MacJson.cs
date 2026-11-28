using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Thermi.Sensors.MacOS.Source;

/// <summary>
/// How a recording keeps a second of what a Mac answered, a file for each source in the second's folder: the SMC's keys
/// and values, the HID sensors, IOReport's channels, the registry's properties by their class, the adapters, and the rest,
/// the clocks, sysctl's values and Mach's counters. A file that does not read as its record is no answer, never a crash.
/// </summary>
internal static class MacJson
{
    public const string SmcFile = "smc.json";

    public const string HidFile = "hid.json";

    public const string ReportFile = "ioreport.json";

    public const string RegistryFile = "registry.json";

    public const string NetworkFile = "network.json";

    public const string SystemFile = "system.json";

    /// <summary>The property that holds a registry entry's own name, as <c>pmgr</c>, which IOKit keeps apart from its properties.</summary>
    public const string EntryName = "IORegistryEntryName";

    /// <summary>A registry key searched up a service's parents, and one searched down its children.</summary>
    public const string Parent = "parent:";

    public const string Child = "child:";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Write(JsonNode node) => node.ToJsonString(Indented);

    public static string Write(IReadOnlyList<HidReading> hid) => JsonSerializer.Serialize<HidReading[]>([.. hid], MacJsonContext.Default.HidReadingArray);

    public static string Write(IReadOnlyList<ReportChannel> report) =>
        JsonSerializer.Serialize<ReportChannel[]>([.. report], MacJsonContext.Default.ReportChannelArray);

    public static string Write(IReadOnlyList<Adapter> adapters) => JsonSerializer.Serialize<Adapter[]>([.. adapters], MacJsonContext.Default.AdapterArray);

    public static string Write(SystemAnswers system) => JsonSerializer.Serialize(system, MacJsonContext.Default.SystemAnswers);

    public static JsonObject ReadObject(string? json)
    {
        try
        {
            return json is null ? [] : JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static HidReading[] ReadHid(string? json) => Parse(json, MacJsonContext.Default.HidReadingArray) ?? [];

    public static ReportChannel[] ReadReport(string? json) => Parse(json, MacJsonContext.Default.ReportChannelArray) ?? [];

    public static Adapter[] ReadAdapters(string? json) => Parse(json, MacJsonContext.Default.AdapterArray) ?? [];

    public static SystemAnswers ReadSystem(string? json) => Parse(json, MacJsonContext.Default.SystemAnswers) ?? new SystemAnswers();

    /// <summary>A number of a JSON value, or null for anything else, as a key whose read failed.</summary>
    public static double? Number(JsonNode? node) =>
        node is not JsonValue value ? null
        : value.TryGetValue<double>(out var real) ? real
        : value.TryGetValue<long>(out var whole) ? whole
        : null;

    /// <summary>A string of a JSON value, or null for anything else.</summary>
    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static T? Parse<T>(string? json, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return json is null ? null : JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A second's answers that come from no source of their own: the clocks, sysctl's values and Mach's counters.</summary>
internal sealed record SystemAnswers
{
    public Moment? Moment { get; init; }

    public Dictionary<string, JsonNode?> Sysctl { get; init; } = new(StringComparer.Ordinal);

    public long[][]? CpuTicks { get; init; }

    public MemoryPages? Memory { get; init; }
}

// A value a source did not give is left out of the file, so a record's parameter it fills reads as missing, never as an error.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(HidReading[]))]
[JsonSerializable(typeof(ReportChannel[]))]
[JsonSerializable(typeof(Adapter[]))]
[JsonSerializable(typeof(SystemAnswers))]
internal sealed partial class MacJsonContext : JsonSerializerContext;
