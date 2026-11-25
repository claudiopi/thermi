using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Thermi.Core.Agents;

/// <summary>
/// The contract's JSON Schemas (<c>docs/agents/README.md</c>), one for each document other programs read, made from the
/// answers' own records so the two cannot drift: a watch's every line is a read's document.
/// </summary>
public static class AgentSchemas
{
    /// <summary>Each schema by the name of its file, as <c>docs/agents/schema</c> holds them.</summary>
    public static IReadOnlyDictionary<string, JsonNode> Current { get; } = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
    {
        ["status"] = Of(AgentJsonContext.Default.StatusAnswer, "status"),
        ["read"] = Of(AgentJsonContext.Default.ReadAnswer, "read and each line of watch"),
        ["sensors"] = Of(AgentJsonContext.Default.SensorsAnswer, "sensors"),
    };

    private static JsonObject Of(JsonTypeInfo type, string command)
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        var named = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = $"thermi-cli {command}, schema {AgentSchema.Current}",
        };
        foreach (var (key, value) in schema.AsObject().ToList())
        {
            schema.AsObject().Remove(key);
            named[key] = value;
        }

        return named;
    }
}
