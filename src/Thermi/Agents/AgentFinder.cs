using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;
using Thermi.Overlay;
using Thermi.Ui.Views;

namespace Thermi.Agents;

/// <summary>The fan finder as the window runs it, fed by a connection of its own, its pick made the CPU fan only when asked.</summary>
internal sealed partial class AgentCommands
{
    // An agent that calls the finder in a loop would keep every core busy: its load starts at most once in this long, which
    // the file of when it last started keeps across the agents' processes.
    private static readonly TimeSpan FinderRest = TimeSpan.FromMinutes(2);

    // One at a time with the other changes, as its pick may be one.
    private Task<AgentReply> FindCpuFanAsync(bool use, CancellationToken stop) => OneAtATimeAsync(
        () => RestLeft() is { } left ? Task.FromResult(Resting(left)) : FindRestedAsync(use, stop),
        stop);

    private async Task<AgentReply> FindRestedAsync(bool use, CancellationToken stop)
    {
        // The window's finder lives on the window's thread; here its timer and this loop take turns under one gate.
        var gate = new Lock();
        var feed = environment.Sensors();
        var finder = new WindowFinder(
            load =>
            {
                Started();
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

    private async Task<AgentReply> FindAsync(WindowFinder finder, ISensorFeed feed, Lock gate, bool use, CancellationToken stop)
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

    private static FanFinderResult? ResultOf(WindowFinder finder, Lock gate)
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

    // A file that cannot be read, or says a time to come, holds nothing back: the rest guards against a loop, not the person.
    private TimeSpan? RestLeft()
    {
        try
        {
            var ran = DateTimeOffset.Parse(File.ReadAllText(environment.FinderRan), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var left = ran + FinderRest - time.GetUtcNow();
            return left > TimeSpan.Zero && left <= FinderRest ? left : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private void Started()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(environment.FinderRan)!);
            File.WriteAllText(environment.FinderRan, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The load still runs: without its file, only the next run's rest is lost.
            FinderRanLost(environment.Log, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "When the fan finder ran could not be kept")]
    private static partial void FinderRanLost(ILogger log, Exception error);
}
