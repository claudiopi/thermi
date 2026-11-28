using System.Text.Json.Nodes;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The page's tests fake the C# side, but decide nothing it decides: their fake replays, from the oracle's table, what
/// the real bridge answered each edit, undo, redo and look of theirs, from the same start, and each search. This holds every answer of
/// the table to what the real bridge answers now, so the fake can never drift from it. With THERMI_ORACLE=write it takes
/// in what the page's tests asked and lacked, and writes the answers instead; =prune also drops what they no longer use.
/// <c>pwsh scripts/oracle.ps1</c> runs both sides until the table holds every call.
/// </summary>
public sealed class BridgeOracleTests
{
    [Fact]
    public async Task TheFakeBridgeAnswersAsTheRealOne()
    {
        var table = new OracleTable();
        var mode = Environment.GetEnvironmentVariable("THERMI_ORACLE");
        var writes = mode is "write" or "prune";
        if (writes)
        {
            table.Merge();
        }

        if (mode == "prune")
        {
            table.Prune();
        }

        var stale = new List<string>();
        foreach (var (id, start) in table.Starts)
        {
            var (shown, worn, _) = await OracleReplay.RunAsync(start!.AsObject(), []);
            if (!JsonNode.DeepEquals(start["shown"], shown) || !JsonNode.DeepEquals(start["worn"], worn))
            {
                stale.Add($"the settings start {id} shows");
                start["shown"] = shown?.DeepClone();
                start["worn"] = worn?.DeepClone();
            }
        }

        foreach (var (index, one) in table.Cases.Index())
        {
            var calls = one?["calls"]?.AsArray() ?? [];
            var (_, _, answers) = await OracleReplay.RunAsync(table.StartOf(one), calls);
            foreach (var (at, answer) in answers.Index().Where(each => !JsonNode.DeepEquals(calls[each.Index]?["answer"], each.Item)))
            {
                stale.Add($"call {at + 1} of case {index}, {calls[at]?["method"]}");
                calls[at]!["answer"] = answer;
            }
        }

        // A call that holds whatever came before it answers the same from any start, so all of them run from the default one;
        // the fake catalog's search and the desktop's part of the status are Core's, from what they name.
        var pure = table.Pure.OfType<JsonObject>().ToList();
        var asked = pure
            .Where(call => !OracleReplay.AnsweredByCore(call))
            .Select(call => new JsonObject { ["method"] = call["method"]?.DeepClone(), ["args"] = call["args"]?.DeepClone() });
        var (_, _, replayed) = await OracleReplay.RunAsync(DefaultStart(), [.. asked]);
        Assert.All(replayed, answer => Assert.Null(answer["error"]));
        var bridged = new Queue<JsonNode?>(replayed.Select(answer => answer["result"]));
        var results = pure.Select(call => OracleReplay.AnsweredByCore(call) ? OracleReplay.CoreAnswer(call) : bridged.Dequeue()).ToList();
        var changed = results.Index().Where(each => !JsonNode.DeepEquals(pure[each.Index]["answer"], each.Item));
        foreach (var (at, result) in changed)
        {
            stale.Add($"pure call {at + 1}, {pure[at]["method"]}");
            pure[at]["answer"] = result?.DeepClone();
        }

        if (writes)
        {
            table.Write();
        }

        Assert.True(
            writes || stale.Count == 0,
            $"{stale.Count} answers of the oracle are not what the real bridge answers now, the first: {stale.FirstOrDefault()}. "
            + "Run pwsh scripts/oracle.ps1, and check the answers it writes.");
    }

    private static JsonObject DefaultStart() => new()
    {
        ["settings"] = new JsonObject(),
        ["looks"] = new JsonObject(),
        ["sensors"] = "admin",
        ["service"] = "running",
        ["texts"] = "en",
        ["problem"] = null,
    };
}
