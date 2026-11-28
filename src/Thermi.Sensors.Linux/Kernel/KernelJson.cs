using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// How a recorded tree keeps what is no file under <c>/sys</c> or <c>/proc</c>, a second's JSON beside its files: the clocks
/// and the address, where each link led, and NVML's answers, in a file named for NVML alone, so a tree without NVIDIA's
/// driver is the same tree with that file gone. A file that does not read as its record is no answer, never a crash.
/// </summary>
internal static class KernelJson
{
    public const string MomentFile = "system.json";

    public const string LinksFile = "links.json";

    public const string NvmlFile = "nvml.json";

    public static string Write(Moment moment) => JsonSerializer.Serialize(moment, KernelJsonContext.Default.Moment);

    public static string Write(IReadOnlyDictionary<string, string> links) =>
        JsonSerializer.Serialize(links.ToDictionary(StringComparer.Ordinal), KernelJsonContext.Default.DictionaryStringString);

    public static string Write(IReadOnlyList<GpuAnswer> gpus) => JsonSerializer.Serialize<GpuAnswer[]>([.. gpus], KernelJsonContext.Default.GpuAnswerArray);

    public static Moment? ReadMoment(string? json) => Parse(json, KernelJsonContext.Default.Moment);

    public static Dictionary<string, string> ReadLinks(string? json) =>
        Parse(json, KernelJsonContext.Default.DictionaryStringString) is { } links && links.Values.All(target => target is not null)
            ? links
            : new Dictionary<string, string>(StringComparer.Ordinal);

    public static GpuAnswer[]? ReadNvml(string? json) =>
        Parse(json, KernelJsonContext.Default.GpuAnswerArray) is { } gpus && gpus.All(gpu => gpu?.Name is not null) ? gpus : null;

    private static T? Parse<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Moment))]
[JsonSerializable(typeof(GpuAnswer[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class KernelJsonContext : JsonSerializerContext;
