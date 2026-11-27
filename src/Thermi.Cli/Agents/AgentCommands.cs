using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Cli.Agents;

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
internal sealed partial class AgentCommands(IAgentEnvironment environment, TimeProvider time) : IDisposable
{
    // An MCP client may call tools at once; the changes go one at a time, so each is its own step and History stays whole.
    private readonly SemaphoreSlim _changing = new(1, 1);

    // An edit as the log keeps it: its text as the agent wrote it, any letter included.
    private static readonly JsonSerializerOptions OneLine = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Each agent edit is a step of its own, however soon the next comes: an undo takes back one call.
    public UndoHistory History { get; } = new();

    public void Dispose() => _changing.Dispose();

    public async Task<AgentReply> RunAsync(AgentRequest request, CancellationToken stop)
    {
        // What the person lets agents do is read first, so a change that writes no settings is refused as well.
        var reply = request.Writes && await HeldAsync() is { } held ? held : await AnswerAsync(request, stop);
        if (reply.Exit == AgentExit.Done && Change(request) is var (did, change))
        {
            Changed(environment.Log, did, change);
        }

        return reply;
    }

    // What a command changed, for the log, or null for one that only read or said; a look file's text is not repeated.
    private static (AgentDid, string)? Change(AgentRequest request) => request switch
    {
        { Verb: AgentVerb.Edit } => (AgentDid.Edit, Compact(request.Text ?? "")),
        { Verb: AgentVerb.Look, Look: LookAction.Export } => null,
        { Verb: AgentVerb.Look, Look: LookAction.Import } => (AgentDid.Look, $"import on overlay {request.Overlay ?? 0}"),
        { Verb: AgentVerb.Look } => (AgentDid.Look, Spaced($"{request.Look}".ToLowerInvariant(), request.Text, request.To, $"{request.Overlay}")),
        { Verb: AgentVerb.FindCpuFan, Switch: true } => (AgentDid.FindCpuFan, "its fan as the CPU fan"),
        { Verb: AgentVerb.StartWithWindows, Switch: { } on } => (AgentDid.StartWithWindows, on ? "on" : "off"),
        _ => null,
    };

    // An edit an agent wrote over several lines is one line of the log, which the window reads back line by line.
    private static string Compact(string json) => JsonNode.Parse(json)?.ToJsonString(OneLine) ?? json;

    private static string Spaced(params string?[] parts) => string.Join(' ', parts.Where(part => part is { Length: > 0 }));

    [LoggerMessage(Level = LogLevel.Information, Message = AgentActivity.Message)]
    private static partial void Changed(ILogger log, AgentDid did, string what);

    private Task<AgentReply> AnswerAsync(AgentRequest request, CancellationToken stop) => request.Verb switch
    {
        AgentVerb.Status => StatusAsync(stop),
        AgentVerb.Read => ReadAsync(request, stop),
        AgentVerb.Sensors => SensorsAsync(request, stop),
        AgentVerb.Config => ConfigAsync(),
        AgentVerb.Monitors =>
            Task.FromResult(new AgentReply(AgentExit.Done, AgentJson.Write(new MonitorsAnswer([.. environment.Screens().Select(MonitorView.Of)])))),
        AgentVerb.Looks => Task.FromResult(Looks()),
        AgentVerb.Preview => PreviewAsync(request, stop),
        AgentVerb.Edit => EditAsync(request.Text ?? "", stop),
        AgentVerb.Look => OneAtATimeAsync(() => LookAsync(request), stop),
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
        Snapshot? first = null;
        try
        {
            await foreach (var snapshot in AgentSamples.StreamAsync(feed, time, stop))
            {
                if (snapshot is null)
                {
                    return Unread(feed.State, feed.Taken, snapshot);
                }

                // A sensor of the first line that drops out of a later one reads as missing, never as a name of nothing.
                first ??= snapshot;
                var answer = AgentReads.Read([snapshot], (await SettingsAsync()).Config, ids, first);
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
        var answer = new ConfigAnswer(JsonNode.Parse(ThermiConfigJson.Write(read.Config))!, read.Problem?.Kind)
        {
            Detail = read.Problem is { } problem ? problem.OneLine(Core.Translations.Texts.English, System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return new AgentReply(read.Problem is null ? AgentExit.Done : AgentExit.SettingsBroken, AgentJson.Write(answer), answer.Detail);
    }

    // Settings that cannot be read paint no overlay of the person's; they and the overlay are checked before the service is asked.
    private async Task<AgentReply> PreviewAsync(AgentRequest request, CancellationToken stop)
    {
        var settings = await SettingsAsync();
        var overlay = request.Overlay ?? 0;
        if (settings.Problem is { } problem)
        {
            return Broken(problem);
        }

        if (overlay < 0 || overlay >= settings.Config.Overlays.Count)
        {
            var last = settings.Config.Overlays.Count - 1;
            var said = string.Create(CultureInfo.InvariantCulture, $"The settings have overlays 0 to {last}; config lists them.");
            return new AgentReply(AgentExit.BadArguments, Error: said);
        }

        var feed = environment.Sensors();
        var (samples, state, taken) = await AgentSamples.TakeAsync(feed, 1, time, stop);
        if (samples is not [var snapshot])
        {
            return Unread(state, taken, null);
        }

        var painted = environment.Paint(settings.Config, overlay, snapshot, request.Scale);
        return new AgentReply(AgentExit.Done, AgentJson.Write(new PreviewAnswer(overlay, painted.Width, painted.Height, Convert.ToBase64String(painted.Png))));
    }

    private AgentFacts Facts(Core.Tray.ServiceState state, bool taken, Snapshot? latest, ConfigProblem? settings) =>
        new(state, environment.Installed(), taken, latest, settings)
        {
            Overlay = environment.Overlay(),
            SensorsOnly = environment.SensorsOnly(),
            PerUser = environment.PerUser(),
        };

    private AgentReply Unread(Core.Tray.ServiceState state, bool taken, Snapshot? latest)
    {
        var problem = AgentStatus.Unread(Facts(state, taken, latest, settings: null)) ?? AgentStatus.Said(AgentProblem.ServiceStale);
        return new AgentReply(AgentExit.NoSensors, Error: $"{problem.Cause} {problem.Fix}");
    }

    private static AgentReply UnknownIds(ReadAnswer answer) =>
        new(
            AgentExit.BadArguments,
            Error: $"No metric, sensor or item is named {string.Join(", ", answer.Unknown)}; sensors lists the sensors, config the items.");

    // No file yet is the defaults at once, with no settle to wait out.
    private async Task<ConfigRead> SettingsAsync() =>
        File.Exists(environment.SettingsPath) ? await SettledAsync() : new ConfigRead(ThermiConfig.New, null, Outside: true);

    // A file missing is waited out as an editor's save, so what the person lets agents do never reads as the defaults' meanwhile.
    private async Task<ConfigRead> SettledAsync()
    {
        using var store = new ConfigStore(environment.SettingsPath, time, environment.RunsInstalled);
        return await store.ReadOnlyAsync();
    }
}
