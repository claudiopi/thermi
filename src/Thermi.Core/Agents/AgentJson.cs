using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Bridge;

namespace Thermi.Core.Agents;

/// <summary>
/// The answers as the contract writes them: camel case, nulls left out, choices as kebab-case words, as the bridge writes
/// its own, and numbers never as strings; a sensor's NaN or infinity never reaches an answer, which reads it as null.
/// </summary>
public static class AgentJson
{
    public static string Write<T>(T answer)
        where T : notnull => JsonSerializer.Serialize(answer, typeof(T), AgentJsonContext.Default);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(BridgeEnums)])]
[JsonSerializable(typeof(StatusAnswer))]
[JsonSerializable(typeof(ReadAnswer))]
[JsonSerializable(typeof(SensorsAnswer))]
[JsonSerializable(typeof(ConfigAnswer))]
[JsonSerializable(typeof(EditAnswer))]
[JsonSerializable(typeof(MonitorsAnswer))]
[JsonSerializable(typeof(LooksAnswer))]
[JsonSerializable(typeof(FinderAnswer))]
[JsonSerializable(typeof(StartAnswer))]
[JsonSerializable(typeof(PreviewAnswer))]
internal sealed partial class AgentJsonContext : JsonSerializerContext;
