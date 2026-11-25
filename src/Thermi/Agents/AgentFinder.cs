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
        var gate = new Lock();
        using var finder = new WindowFinder(environment.Load, time, turn => { lock (gate) { turn(); } }, () => { });
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
                    var answer = new FinderAnswer(Outcome: null, Used: false) { Refused = refused };
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

            if (finder.Result is not null)
            {
                break;
            }
        }

        return finder.Result is { } result ? await FoundAsync(result, last!, use) : Unread(feed.State, feed.Taken, last);
    }

    private async Task<AgentReply> FoundAsync(FanFinderResult result, Snapshot snapshot, bool use)
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

        var used = await OneAtATimeAsync(() => WriteAsync(config => ItemEdits.UseCpuFan(config, 0, snapshot, found)));
        return used.Exit == AgentExit.Done ? used with { Json = AgentJson.Write(answer with { Used = true }) } : used;
    }
}
