using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The fake bridge's oracle, <c>src/Thermi.Settings.Web/tests/oracle/edits.json</c>: the starts the page's tests open the
/// window on, by an id the page's side gives each, and the cases, each a start and the calls the page made from it, with
/// the real bridge's answers. The page's tests write what they asked and the table lacked, and what they used, under
/// their test results; <see cref="Merge"/> and <see cref="Prune"/> take those in.
/// </summary>
internal sealed class OracleTable
{
    private static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _web = Path.Combine(TestRecordings.RepoRoot(), "src", "Thermi.Settings.Web");
    private readonly JsonObject _root;

    public OracleTable()
    {
        _root = JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? [];
    }

    public JsonObject Starts => _root["starts"]?.AsObject() ?? throw new InvalidDataException("The oracle has no starts.");

    public JsonArray Cases => _root["cases"]?.AsArray() ?? throw new InvalidDataException("The oracle has no cases.");

    /// <summary>The calls whose answer holds whatever came before them, as a search, each with its answer, from no start of their own.</summary>
    public JsonArray Pure
    {
        get
        {
            _root["pure"] ??= new JsonArray();
            return _root["pure"]!.AsArray();
        }
    }

    private string FilePath => Path.Combine(_web, "tests", "oracle", "edits.json");

    private string Results => Path.Combine(_web, "test-results", "oracle-misses");

    public JsonObject StartOf(JsonNode? one) => Starts[one?["start"]?.GetValue<string>() ?? ""]?.AsObject()
        ?? throw new InvalidDataException("A case of the oracle names no start it has.");

    /// <summary>
    /// Takes in what the page's tests asked and the table lacked: a start it had not, and calls from a start, which extend
    /// the case they go on from or make a case of their own. Calls a case already holds change nothing.
    /// </summary>
    public void Merge()
    {
        var misses = Lines("misses").OfType<JsonObject>().OrderBy(miss => miss["calls"]?.AsArray().Count ?? 0).ToList();
        foreach (var pure in misses.Select(miss => miss["pure"]).OfType<JsonObject>())
        {
            if (!Pure.Any(one => JsonNode.DeepEquals(one?["method"], pure["method"]) && JsonNode.DeepEquals(one?["args"], pure["args"])))
            {
                Pure.Add(new JsonObject { ["method"] = pure["method"]?.DeepClone(), ["args"] = pure["args"]?.DeepClone() });
            }
        }

        foreach (var miss in misses.Where(miss => miss["pure"] is null))
        {
            var id = miss["start"]?.GetValue<string>() ?? "";
            Starts[id] ??= miss["state"]?.DeepClone();
            var calls = miss["calls"]?.AsArray() ?? [];
            var same = Cases.OfType<JsonObject>().Where(one => one["start"]?.GetValue<string>() == id).ToList();
            if (calls.Count == 0 || same.Any(one => Leads(calls, one["calls"]?.AsArray() ?? [])))
            {
                continue;
            }

            var extended = same.FirstOrDefault(one => Leads(one["calls"]?.AsArray() ?? [], calls));
            var asked = new JsonArray(
            [
                .. calls.Select(call => new JsonObject
                {
                    ["method"] = call?["method"]?.DeepClone(),
                    ["args"] = call?["args"]?.DeepClone(),
                }),
            ]);
            if (extended is null)
            {
                Cases.Add(new JsonObject { ["start"] = id, ["calls"] = asked });
            }
            else
            {
                extended["calls"] = asked;
            }
        }
    }

    /// <summary>Drops the calls and the starts the page's tests no longer use, after a run of all of them.</summary>
    public void Prune()
    {
        var used = Lines("used").OfType<JsonObject>().ToList();

        // A run whose tests never started used nothing, and would leave an empty table.
        Assert.NotEmpty(used);
        var depths = used.SelectMany(one => one["cases"]?.AsArray() ?? []).OfType<JsonArray>()
            .GroupBy(pair => pair[0]!.GetValue<int>(), pair => pair[1]!.GetValue<int>())
            .ToDictionary(group => group.Key, group => group.Max());
        var cases = Cases.Select((one, index) => (One: one!.AsObject(), Depth: depths.GetValueOrDefault(index))).Where(each => each.Depth > 0).ToList();
        Cases.Clear();
        foreach (var (one, depth) in cases)
        {
            var calls = one["calls"]!.AsArray();
            while (calls.Count > depth)
            {
                calls.RemoveAt(calls.Count - 1);
            }

            Cases.Add(one.DeepClone());
        }

        var pure = used.SelectMany(one => one["pure"]?.AsArray() ?? []).Select(index => index!.GetValue<int>()).ToHashSet();
        var kept = Pure.Where((_, index) => pure.Contains(index)).Select(one => one!.DeepClone()).ToList();
        Pure.Clear();
        kept.ForEach(Pure.Add);

        var starts = used.SelectMany(one => one["starts"]?.AsArray() ?? []).Select(start => start?.GetValue<string>()).ToHashSet();
        foreach (var id in Starts.Select(pair => pair.Key).Where(id => !starts.Contains(id)).ToList())
        {
            Starts.Remove(id);
        }
    }

    /// <summary>Writes the table back, a start or a call to a line, and forgets what the tests asked.</summary>
    public void Write()
    {
        var text = new StringBuilder("{\n  \"starts\": {\n");
        text.AppendJoin(",\n", Starts.Select(pair => $"    {JsonSerializer.Serialize(pair.Key)}: {pair.Value?.ToJsonString(Written)}"));
        text.Append("\n  },\n  \"cases\": [\n");
        text.AppendJoin(
            ",\n",
            Cases.Select(one =>
                $"    {{ \"start\": {one?["start"]?.ToJsonString()}, \"calls\": [\n"
                + string.Join(",\n", (one?["calls"]?.AsArray() ?? []).Select(call => $"      {call?.ToJsonString(Written)}"))
                + "\n    ] }"));
        text.Append("\n  ],\n  \"pure\": [\n");
        text.AppendJoin(",\n", Pure.Select(call => $"    {call?.ToJsonString(Written)}"));
        text.Append("\n  ]\n}\n");
        File.WriteAllText(FilePath, text.ToString());
        if (Directory.Exists(Results))
        {
            Directory.Delete(Results, recursive: true);
        }
    }

    // Whether the first calls of the longer are the shorter's, method and arguments alike.
    private static bool Leads(JsonArray shorter, JsonArray longer) =>
        shorter.Count <= longer.Count
        && shorter
            .Select((call, at) => JsonNode.DeepEquals(call?["method"], longer[at]?["method"])
                && JsonNode.DeepEquals(call?["args"], longer[at]?["args"]))
            .All(same => same);

    private IEnumerable<JsonNode?> Lines(string kind) => Directory.Exists(Results)
        ? Directory.EnumerateFiles(Results, $"{kind}-*.jsonl")
            .SelectMany(File.ReadAllLines)
            .Where(line => line.Length > 0)
            .Select(line => JsonNode.Parse(line))
        : [];
}
