using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Cli.Agents;

/// <summary>The fan finder as the window runs it, fed by a connection of its own, its pick made the CPU fan only when asked.</summary>
internal sealed partial class AgentCommands
{
    // An agent that calls the finder in a loop would keep every core busy: a run starts at most once in this long, which
    // the file of when the last one started keeps across the agents' processes.
    private static readonly TimeSpan FinderRest = TimeSpan.FromMinutes(2);

    // One at a time with the other changes, as its pick may be one.
    private Task<AgentReply> FindCpuFanAsync(bool use, CancellationToken stop) => OneAtATimeAsync(() => ClaimedAsync(use, stop), stop);

    // The file is held through the run, so a run another process starts at once finds it taken and rests too.
    private async Task<AgentReply> ClaimedAsync(bool use, CancellationToken stop)
    {
        await using var claim = Claim(out var taken);
        if (taken)
        {
            return Resting(FinderRest);
        }

        if (claim is not null && await RestLeftAsync(claim, stop) is { } left)
        {
            return Resting(left);
        }

        return await FindRestedAsync(claim, use, stop);
    }

    private async Task<AgentReply> FindRestedAsync(FileStream? claim, bool use, CancellationToken stop)
    {
        // The window's finder lives on the window's thread; here its timer and this loop take turns under one gate.
        var gate = new Lock();
        var feed = environment.Sensors();
        // The rest counts from a load that started: a finder that cannot start loads nothing.
        var finder = new FinderRun(
            load =>
            {
                Stamp(claim);
                environment.Load(load);
            },
            time,
            turn => { lock (gate) { turn(); } },
            () => { },
            environment.Log,
            () => feed.State);
        try
        {
            return await FindAsync(finder, feed, gate, use, stop);
        }
        finally
        {
            lock (gate)
            {
                finder.Dispose();
            }
        }
    }

    private async Task<AgentReply> FindAsync(FinderRun finder, ISensorFeed feed, Lock gate, bool use, CancellationToken stop)
    {
        Snapshot? last = null;
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

        return ResultOf(finder, gate) is { } result ? await FoundAsync(result, last!, use) : Unread(feed.State, feed.Taken, last);
    }

    private static FanFinderResult? ResultOf(FinderRun finder, Lock gate)
    {
        lock (gate)
        {
            return finder.Result;
        }
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

        // A fan found that the settings refuse to take, as with no CPU fan item, is still the finder's answer, with why.
        Refusal? refused = null;
        var used = await WriteAsync(config =>
        {
            var edit = ItemEdits.UseCpuFan(config, 0, snapshot, found);
            refused = edit.Refused;
            return edit;
        });
        return used.Exit == AgentExit.Done ? used with { Json = AgentJson.Write(answer with { Used = true }) }
            : used.Exit == AgentExit.Refused && refused is { } why ? used with { Json = AgentJson.Write(answer with { Refusal = Texts.Key(why) }) }
            : used;
    }

    private static AgentReply Resting(TimeSpan left)
    {
        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        return new AgentReply(
            AgentExit.Refused,
            AgentJson.Write(new FinderAnswer(Outcome: null, Used: false) { RunsAgainIn = seconds }),
            $"The fan finder loaded the CPU less than {FinderRest.TotalMinutes} minutes ago; it runs again in {seconds} s.");
    }

    // Null when another process holds it, or when Windows refuses it: the rest guards against a loop, not the person.
    private FileStream? Claim(out bool taken)
    {
        taken = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(environment.FinderRan)!);
            return new FileStream(environment.FinderRan, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (UnauthorizedAccessException ex)
        {
            FinderRanLost(environment.Log, ex);
            return null;
        }
        catch (IOException ex) when (environment.HeldElsewhere(ex))
        {
            taken = true;
            return null;
        }
        catch (IOException ex)
        {
            FinderRanLost(environment.Log, ex);
            return null;
        }
    }

    private void Stamp(FileStream? claim)
    {
        if (claim is null)
        {
            return;
        }

        try
        {
            claim.SetLength(0);
            claim.Write(System.Text.Encoding.UTF8.GetBytes(time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));
            claim.Flush();
        }
        catch (IOException ex)
        {
            // The load still runs: only the next run's rest is lost.
            FinderRanLost(environment.Log, ex);
        }
    }

    // A file empty or unreadable, or one that says a time to come, holds nothing back.
    private async Task<TimeSpan?> RestLeftAsync(FileStream claim, CancellationToken stop)
    {
        using var reader = new StreamReader(claim, leaveOpen: true);
        string said;
        try
        {
            said = await reader.ReadToEndAsync(stop);
        }
        catch (IOException ex)
        {
            FinderRanLost(environment.Log, ex);
            return null;
        }

        if (!DateTimeOffset.TryParse(said, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ran))
        {
            return null;
        }

        var left = ran + FinderRest - time.GetUtcNow();
        return left > TimeSpan.Zero && left <= FinderRest ? left : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "When the fan finder ran could not be read or kept")]
    private static partial void FinderRanLost(ILogger log, Exception error);
}
