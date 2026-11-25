using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thermi.Tests.Contract;

/// <summary>
/// The part of JSON Schema the contract's schemas use, as .NET's exporter writes them: a type or a list of types, an enum,
/// an object's properties and required ones, an array's items. A property the schema does not name is a finding too, so
/// the schemas name every field the answers hold.
/// </summary>
internal static class SchemaCheck
{
    /// <summary>Where <paramref name="value"/> breaks <paramref name="schema"/>, by its path in the document.</summary>
    public static IEnumerable<string> Findings(JsonNode schema, JsonNode? value, string path = "$")
    {
        if (schema["enum"] is JsonArray choices)
        {
            return choices.Any(choice => JsonNode.DeepEquals(choice, value)) ? [] : [$"{path} is {value?.ToJsonString() ?? "null"}, not one of {choices.ToJsonString()}"];
        }

        var types = schema["type"] switch
        {
            JsonArray many => many.Select(type => (string)type!).ToArray(),
            JsonValue one => [(string)one!],
            _ => [],
        };
        if (types.Length > 0 && !types.Any(type => Is(type, value)))
        {
            return [$"{path} is {Kind(value)}, not {string.Join(" or ", types)}"];
        }

        return value switch
        {
            JsonObject fields => OfObject(schema, fields, path),
            JsonArray items when schema["items"] is { } each => items.SelectMany((item, at) => Findings(each, item, $"{path}[{at}]")),
            _ => [],
        };
    }

    private static IEnumerable<string> OfObject(JsonNode schema, JsonObject fields, string path)
    {
        var properties = schema["properties"]?.AsObject();
        var missing = (schema["required"]?.AsArray() ?? []).Select(name => (string)name!).Where(name => !fields.ContainsKey(name))
            .Select(name => $"{path} lacks {name}");
        var unnamed = fields.Where(field => properties?.ContainsKey(field.Key) != true).Select(field => $"{path}.{field.Key} is not in the schema");
        var wrong = fields.Where(field => properties?.ContainsKey(field.Key) == true)
            .SelectMany(field => Findings(properties![field.Key]!, field.Value, $"{path}.{field.Key}"));
        return [.. missing, .. unnamed, .. wrong];
    }

    private static bool Is(string type, JsonNode? value) => type switch
    {
        "null" => value is null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value?.GetValueKind() == JsonValueKind.String,
        "boolean" => value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "number" => value?.GetValueKind() == JsonValueKind.Number,
        "integer" => value?.GetValueKind() == JsonValueKind.Number && double.IsInteger((double)value),
        _ => throw new InvalidOperationException($"The contract's schemas use no type {type}."),
    };

    private static string Kind(JsonNode? value) => value?.GetValueKind().ToString().ToLowerInvariant() ?? "null";
}
