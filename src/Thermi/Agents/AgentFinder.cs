using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;
using Thermi.Ui.Views;

namespace Thermi.Agents;

/// <summary>The fan finder as the window runs it, fed by a connection of its own, its pick made the CPU fan only when asked.</summary>
internal sealed partial class AgentCommands
{
    private async Task<AgentReply> FindCpuFanAsync(bool use, CancellationToken stop)
    {
        // The window's finder lives on the window's thread; here its timer and this loop take turns under one gate.
        var gate = new Lock();
        var finder = new WindowFinder(environment.Load, time, turn => { lock (gate) { turn(); } }, () => { });
        try
        {
            return await FindAsync(finder, gate, use, stop);
        }
        finally
        {
            lock (gate)
            {
                finder.Dispose();
            }
        }
    }

    private async Task<AgentReply> FindAsync(WindowFinder finder, Lock gate, bool use, CancellationToken stop)
    {
        Snapshot? last = null;
        var feed = environment.Sensors();
        await foreach (var snapshot in AgentSamples.StreamAsync(feed, time, stop))
        {
            if (snapshot is null)
            {
                break;
            }

            lock (gate)
            {
                if (last is null && FanFinder.CannotStart(snapshot) is { } refused)
                {
                    var answer = new FinderAnswer(Outcome: null, Used: false) { CannotStart = refused };
                    return new AgentReply(AgentExit.Refused, AgentJson.Write(answer), Texts.English.Of(refused));
                }

                if (last is null)
                {
                    finder.Start(snapshot);
                }
                else
                {
                    finder.Add(snapshot);
                }

                last = snapshot;
            }

            if (ResultOf(finder, gate) is not null)
            {
                break;
            }
        }

        return ResultOf(finder, gate) is { } result ? await FoundAsync(result, last!, use, stop) : Unread(feed.State, feed.Taken, last);
    }

    private static FanFinderResult? ResultOf(WindowFinder finder, Lock gate)
    {
        lock (gate)
        {
            return finder.Result;
        }
    }

    private async Task<AgentReply> FoundAsync(FanFinderResult result, Snapshot snapshot, bool use, CancellationToken stop)
    {
        var answer = new FinderAnswer(result.Outcome, Used: false)
        {
            Fan = result.Fan,
            Name = result.Fan is { } fan && SensorMap.Find(snapshot, fan) is { } sensor ? sensor.Name : null,
        };
        if (!use || result.Fan is not { } found)
        {
            return result.Fan is null
                ? new AgentReply(AgentExit.Refused, AgentJson.Write(answer), Texts.English.Of(result.Outcome))
                : new AgentReply(AgentExit.Done, AgentJson.Write(answer));
        }

        // A fan found that the settings refuse to take, as with no CPU fan item, is still the finder's answer, with why.
        Refusal? refused = null;
        var used = await OneAtATimeAsync(
            () => WriteAsync(config =>
            {
                var edit = ItemEdits.UseCpuFan(config, 0, snapshot, found);
                refused = edit.Refused;
                return edit;
            }),
            stop);
        return used.Exit == AgentExit.Done ? used with { Json = AgentJson.Write(answer with { Used = true }) }
            : used.Exit == AgentExit.Refused && refused is { } why ? used with { Json = AgentJson.Write(answer with { Refusal = Texts.Key(why) }) }
            : used;
    }
}
