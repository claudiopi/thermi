using System.Text.Json.Nodes;
using Thermi.Core.Agents;
using Thermi.Tests.Contract;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The contract other programs read (<c>docs/agents/README.md</c>): every answer validates against its JSON Schema, and a
/// schema only grows from what the last release shipped.
/// </summary>
public sealed class ContractTests
{
    private static readonly string Schemas = Path.Combine(TestFiles.Root, "docs", "agents", "schema");

    // The schemas as the last release shipped them, which today's answers must still meet.
    private static readonly string Shipped = Path.Combine(TestFiles.Root, "tests", "Thermi.Tests", "Contract", "shipped-1");

    [Fact]
    public void AnswersKeepTheContract()
    {
        foreach (var (name, schema) in AgentSchemas.Current)
        {
            Assert.True(
                JsonNode.DeepEquals(schema, Schema(Schemas, name)), $"docs/agents/schema/{name}.schema.json is stale: build src/Thermi to write it again.");
        }

        var findings = ContractAnswers.All()
            .SelectMany(answer => SchemaCheck.Findings(Schema(Schemas, answer.Schema), JsonNode.Parse(answer.Json), answer.Schema));

        Assert.Empty(findings);
    }

    [Fact]
    public void TheContractOnlyGrows()
    {
        var findings = AgentSchemas.Current.Keys
            .SelectMany(name => Kept(Schema(Shipped, name), AgentSchemas.Current[name], name))
            // A field or a choice a reader of the shipped schema has not met is growth: it takes a word it does not know as unknown.
            .Concat(ContractAnswers.All().SelectMany(answer => SchemaCheck.Findings(Schema(Shipped, answer.Schema), JsonNode.Parse(answer.Json), answer.Schema)
                .Where(finding => !finding.EndsWith("is not in the schema", StringComparison.Ordinal) && !finding.Contains(", not one of ", StringComparison.Ordinal))));

        Assert.Empty(findings);
    }

    // Every property the shipped schema named is still there, of the same type, with every choice it had.
    private static IEnumerable<string> Kept(JsonNode? shipped, JsonNode? now, string path)
    {
        if (shipped is null)
        {
            return [];
        }

        if (now is null)
        {
            return [$"{path} is gone"];
        }

        var typed = JsonNode.DeepEquals(shipped["type"], now["type"])
            ? []
            : new[] { $"{path} was {shipped["type"]?.ToJsonString()}, now {now["type"]?.ToJsonString()}" };
        var choices = (shipped["enum"]?.AsArray() ?? []).Where(choice => now["enum"]?.AsArray().Any(each => JsonNode.DeepEquals(each, choice)) != true)
            .Select(choice => $"{path} lost the choice {choice?.ToJsonString() ?? "null"}");
        var fields = (shipped["properties"]?.AsObject() ?? []).SelectMany(field => Kept(field.Value, now["properties"]?[field.Key], $"{path}.{field.Key}"));
        return [.. typed, .. choices, .. fields, .. Kept(shipped["items"], now["items"], $"{path}[]")];
    }

    private static JsonNode Schema(string folder, string name) => JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(folder, $"{name}.schema.json")))!;
}
