using System.Text.Json.Nodes;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The page's tests fake the C# side, but decide nothing it decides: their fake replays, from the oracle's table, what
/// the real bridge answered each edit, undo, redo and look of theirs, from the same start. This holds every answer of
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
            var (shown, _) = await OracleReplay.RunAsync(start!.AsObject(), []);
            if (!JsonNode.DeepEquals(start["shown"], shown))
            {
                stale.Add($"the settings start {id} shows");
                start["shown"] = shown?.DeepClone();
            }
        }

        foreach (var (index, one) in table.Cases.Index())
        {
            var calls = one?["calls"]?.AsArray() ?? [];
            var (_, answers) = await OracleReplay.RunAsync(table.StartOf(one), calls);
            foreach (var (at, answer) in answers.Index().Where(each => !JsonNode.DeepEquals(calls[each.Index]?["answer"], each.Item)))
            {
                stale.Add($"call {at + 1} of case {index}, {calls[at]?["method"]}");
                calls[at]!["answer"] = answer;
            }
        }

        if (writes)
        {
            table.Write();
        }

        Assert.True(writes || stale.Count == 0, $"{stale.Count} answers of the oracle are not what the real bridge answers now, the first: {stale.FirstOrDefault()}. Run pwsh scripts/oracle.ps1, and check the answers it writes.");
    }
}
