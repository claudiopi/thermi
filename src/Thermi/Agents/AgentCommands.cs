using Thermi.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Overlay;

namespace Thermi.Agents;

/// <summary>How an agent's command ended, which is its process's exit code (<c>docs/adr/0013-agents-cli-and-mcp.md</c>).</summary>
internal enum AgentExit
{
    Done = 0,
    Refused = 1,
    BadArguments = 2,
    NoSensors = 3,
    SettingsBroken = 4,
}

/// <summary>A command's answer: its exit, the JSON document it printed, and what it said on stderr.</summary>
internal sealed record AgentReply(AgentExit Exit, string? Json = null, string? Error = null);

/// <summary>
/// Runs an agent's commands, the command line's and the MCP server's alike, each from what Core decides, the readings from
/// a connection of its own and the settings from the file. Edits are kept in <see cref="History"/> for the MCP server's
/// undo; a command line process forgets them as it exits.
/// </summary>
internal sealed partial class AgentCommands(AgentEnvironment environment, TimeProvider time)
{
    public UndoHistory History { get; } = new(time);

    public Task<AgentReply> RunAsync(AgentRequest request, CancellationToken stop) => request.Verb switch
    {
        AgentVerb.Status => StatusAsync(stop),
        AgentVerb.Read => ReadAsync(request, stop),
        AgentVerb.Sensors => SensorsAsync(request, stop),
        AgentVerb.Config => ConfigAsync(),
        AgentVerb.Monitors => Task.FromResult(new AgentReply(AgentExit.Done, AgentJson.Write(new MonitorsAnswer([.. environment.Screens().Select(MonitorView.Of)])))),
        AgentVerb.Looks => Task.FromResult(Looks()),
        AgentVerb.Edit => EditAsync(request.Text ?? "", stop),
        AgentVerb.Look => LookAsync(request),
        AgentVerb.FindCpuFan => FindCpuFanAsync(request.Switch == true, stop),
        AgentVerb.StartWithWindows => Task.FromResult(StartWithWindows(request.Switch)),
        AgentVerb.Watch or AgentVerb.Mcp => throw new ArgumentException($"{request.Verb} is served, not answered.", nameof(request)),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Verb, "Unknown agent command."),
    };

    /// <summary>
    /// <see cref="AgentVerb.Read"/>'s answer once a second, each as one line, until <paramref name="stop"/> or the service
    /// goes quiet, which ends it with the problem; the settings are read again for each line.
    /// </summary>
    public async Task<AgentReply> WatchAsync(IReadOnlyList<string> ids, Func<string, Task> line, CancellationToken stop)
    {
        var feed = environment.Sensors();
        try
        {
            await foreach (var snapshot in AgentSamples.StreamAsync(feed, time, stop))
            {
                if (snapshot is null)
                {
                    return Unread(feed.State, feed.Taken, snapshot);
                }

                var answer = AgentReads.Read([snapshot], (await SettingsAsync()).Config, ids);
                if (answer.Unknown.Count > 0)
                {
                    return UnknownIds(answer);
                }

                await line(AgentJson.Write(answer with { At = time.GetUtcNow() }));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Whoever watched stopped listening, which is how a watch ends.
        }

        return new AgentReply(AgentExit.Done);
    }

    private async Task<AgentReply> StatusAsync(CancellationToken stop)
    {
        var feed = environment.Sensors();
        var (samples, state, taken) = await AgentSamples.TakeAsync(feed, 1, time, stop);
        var settings = await SettingsAsync();
        return new AgentReply(AgentExit.Done, AgentJson.Write(AgentStatus.Of(Facts(state, taken, samples.Count > 0 ? samples[^1] : null, settings.Problem))));
    }

    private async Task<AgentReply> ReadAsync(AgentRequest request, CancellationToken stop)
    {
        var feed = environment.Sensors();
        var (samples, state, taken) = await AgentSamples.TakeAsync(feed, request.Seconds, time, stop);
        if (samples.Count == 0)
        {
            return Unread(state, taken, null);
        }

        var answer = AgentReads.Read(samples, (await SettingsAsync()).Config, request.Ids);
        return answer.Unknown.Count > 0 ? UnknownIds(answer) : new AgentReply(AgentExit.Done, AgentJson.Write(answer with { At = time.GetUtcNow() }));
    }

    private async Task<AgentReply> SensorsAsync(AgentRequest request, CancellationToken stop)
    {
        var feed = environment.Sensors();
        var (samples, state, taken) = await AgentSamples.TakeAsync(feed, 1, time, stop);
        return samples is [var snapshot]
            ? new AgentReply(AgentExit.Done, AgentJson.Write(AgentViews.Sensors(snapshot, (await SettingsAsync()).Config, request.Search, request.Types)))
            : Unread(state, taken, null);
    }

    private async Task<AgentReply> ConfigAsync()
    {
        var read = await SettingsAsync();
        var answer = new ConfigAnswer(System.Text.Json.Nodes.JsonNode.Parse(ThermiConfigJson.Write(read.Config))!, read.Problem?.Kind)
        {
            Detail = read.Problem is { } problem ? problem.OneLine(Core.Translations.Texts.English, System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return new AgentReply(read.Problem is null ? AgentExit.Done : AgentExit.SettingsBroken, AgentJson.Write(answer), answer.Detail);
    }

    private AgentFacts Facts(Core.Tray.ServiceState state, bool taken, Snapshot? latest, ConfigProblem? settings) =>
        new(state, environment.Installed(), taken, latest, settings) { Overlay = environment.Overlay(), SensorsOnly = environment.SensorsOnly() };

    // The service's problem, said with its cause and its fix, as status says it.
    private AgentReply Unread(Core.Tray.ServiceState state, bool taken, Snapshot? latest)
    {
        var problem = AgentStatus.Unread(Facts(state, taken, latest, settings: null)) ?? AgentStatus.Said(AgentProblem.ServiceStale);
        return new AgentReply(AgentExit.NoSensors, Error: $"{problem.Cause} {problem.Fix}");
    }

    private static AgentReply UnknownIds(ReadAnswer answer) =>
        new(AgentExit.BadArguments, Error: $"No metric, sensor or item is named {string.Join(", ", answer.Unknown)}; sensors lists the sensors, config the items.");

    private async Task<ConfigRead> SettingsAsync()
    {
        using var store = new ConfigStore(environment.SettingsPath, time);
        return await store.ReadOnlyAsync();
    }
}
