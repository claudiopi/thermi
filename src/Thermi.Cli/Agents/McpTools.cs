using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Thermi.Cli.Commands;
using Thermi.Core.Sensors;

namespace Thermi.Cli.Agents;

/// <summary>
/// <c>thermi-cli mcp</c>: the agents' commands as an MCP server's tools, through the official SDK, each answering its
/// command's JSON, and a refusal, a bad argument or a service that does not read as a tool error that says why. The
/// session's edits can be undone, only where what they wrote is still there.
/// </summary>
internal static class McpTools
{
    private const string Instructions =
        "thermi reads this PC's sensors through its own service, as the user, and configures its overlay. Values read in fixed units " +
        "(°C, %, MHz, W, RPM, GB, B/s, fps, ms, V); a value that cannot be read is null with why, never 0. An item's level is normal, " +
        "warning or critical, by the thresholds the person set. When status names a problem, tell the person its fix: never start the " +
        "service or install the driver yourself. Edits follow the settings window's rules, and undo takes back this session's own. " +
        "find_cpu_fan loads every core for up to 30 s: run it only when the person asks. When the person lets agents only read, in thermi's " +
        "Settings under AI agents, every change is refused: say so, and leave turning it back on to them.";

    public static async Task<int> ServeAsync(AgentCommands commands, Stream input, Stream output, ILoggerFactory logs, CancellationToken stop)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "thermi", Version = Version() },
            ServerInstructions = Instructions,
            ToolCollection = [.. Tools(commands)],
        };
        await using var transport = new StreamServerTransport(input, output, "thermi", logs);
        await using var server = McpServer.Create(transport, options, logs);
        await server.RunAsync(stop);
        return 0;
    }

    /// <summary>Every tool, its name and whether it only reads; the edits' tools change only the user's own settings.</summary>
    public static IEnumerable<McpServerTool> Tools(AgentCommands commands)
    {
        async Task<CallToolResult> Run(AgentRequest request, CancellationToken stop)
        {
            return Result(await commands.RunAsync(request, stop));
        }

        return [.. Reads(Run), .. Changes(Run), Stepped("undo", "Takes back this session's last edit, where what it wrote is still there.", commands.UndoAsync),
            Stepped("redo", "Makes again the edit undo took back last.", commands.RedoAsync)];
    }

    private static IEnumerable<McpServerTool> Reads(Func<AgentRequest, CancellationToken, Task<CallToolResult>> run)
    {
        yield return McpServerTool.Create(
            (CancellationToken stop) => run(new AgentRequest(AgentVerb.Status), stop),
            Reading("status", "Whether the sensors read, an overlay runs, and each problem with what a person does about it."));
        yield return McpServerTool.Create(
            ([Description("Metrics, sensor ids or item:<id>; every metric and item but network addresses when empty.")] string[]? ids = null,
                [Description("Seconds to sample, 1 to 60.")] int? seconds = null,
                CancellationToken stop = default) =>
                seconds is < 1 or > AgentCommandLine.MaxSeconds
                    ? Task.FromResult(Result(new AgentReply(AgentExit.BadArguments, Error: $"seconds is 1 to {AgentCommandLine.MaxSeconds}.")))
                    : run(new AgentRequest(AgentVerb.Read) { Ids = ids ?? [], Seconds = seconds ?? 1 }, stop),
            Reading("read", "Reads metrics as cpu.temperature, sensor ids and items as item:<id>, now or as minimum, maximum and average."));
        yield return McpServerTool.Create(
            ([Description("Words to find sensors by, as 'cpu temperature'.")] string? search = null,
                [Description("Types, as temperature, load or fan.")] SensorKind[]? types = null,
                CancellationToken stop = default) =>
                run(new AgentRequest(AgentVerb.Sensors) { Search = search, Types = types ?? [] }, stop),
            Reading("sensors", "Lists every sensor the service reads, by device, found as the settings window's search finds."));
        yield return McpServerTool.Create(
            (CancellationToken stop) => run(new AgentRequest(AgentVerb.Config), stop),
            Reading("config", "The settings as thermi reads them, with every overlay and item's id."));
        yield return McpServerTool.Create(
            (CancellationToken stop) => run(new AgentRequest(AgentVerb.Monitors), stop),
            Reading("monitors", "The monitors, their bounds and taskbars, in Windows' pixels."));
        yield return McpServerTool.Create(
            (CancellationToken stop) => run(new AgentRequest(AgentVerb.Looks), stop),
            Reading("looks", "The saved looks' names."));
    }

    private static IEnumerable<McpServerTool> Changes(Func<AgentRequest, CancellationToken, Task<CallToolResult>> run)
    {
        yield return McpServerTool.Create(
            ([Description("One edit with its kind, as {\"kind\":\"threshold\",\"item\":\"cpu.temperature\",\"warning\":70,\"critical\":90}.")] JsonElement edit,
                CancellationToken stop) =>
                run(new AgentRequest(AgentVerb.Edit) { Text = edit.GetRawText() }, stop),
            Changing("edit", "Makes one change of the settings by the settings window's rules; see the README for the kinds.", destructive: true));
        yield return McpServerTool.Create(
            (LookAction action,
                [Description("The saved look's name.")] string? name = null,
                [Description("Its new name, to rename.")] string? to = null,
                [Description("The overlay, counted from 0.")] int? overlay = null,
                [Description("A look file's text, to import.")] string? text = null,
                CancellationToken stop = default) =>
                run(new AgentRequest(AgentVerb.Look) { Look = action, Text = action == LookAction.Import ? text : name, To = to, Overlay = overlay }, stop),
            Changing("look", "Wears, saves, renames, deletes, exports or imports a look.", destructive: true));
        yield return McpServerTool.Create(
            ([Description("Makes the fan found the first overlay's CPU fan.")] bool? use = null, CancellationToken stop = default) =>
                run(new AgentRequest(AgentVerb.FindCpuFan) { Switch = use == true }, stop),
            Changing("find_cpu_fan", "Loads every core for up to 30 s, at most once in 2 minutes, and finds the fan that answers.", destructive: false));
        yield return McpServerTool.Create(
            ([Description("True or false sets it; none only says.")] bool? on = null, CancellationToken stop = default) =>
                run(new AgentRequest(AgentVerb.StartWithWindows) { Switch = on }, stop),
            Changing("start_with_windows", "Says, or sets, whether the overlay starts at sign-in.", destructive: true, idempotent: true));
    }

    private static McpServerToolCreateOptions Reading(string name, string description) =>
        new() { Name = name, Description = description, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false };

    // Destructive where undo cannot take it back: an overlay deleted, the settings reset, a look's file deleted, the start at sign-in.
    private static McpServerToolCreateOptions Changing(string name, string description, bool destructive, bool idempotent = false) =>
        new() { Name = name, Description = description, ReadOnly = false, Destructive = destructive, Idempotent = idempotent, OpenWorld = false };

    private static McpServerTool Stepped(string name, string description, Func<CancellationToken, Task<AgentReply>> step) =>
        McpServerTool.Create(async (CancellationToken stop) => Result(await step(stop)), Changing(name, description, destructive: false));

    private static CallToolResult Result(AgentReply reply) => new()
    {
        IsError = reply.Exit != AgentExit.Done,
        Content = [new TextContentBlock { Text = reply.Exit == AgentExit.Done ? reply.Json ?? "" : Said(reply) }],
    };

    private static string Said(AgentReply reply) => string.Join("\n", new[] { reply.Error, reply.Json }.OfType<string>());

    private static string Version() =>
        typeof(McpTools).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            is [System.Reflection.AssemblyInformationalVersionAttribute version] ? version.InformationalVersion : "0";
}
