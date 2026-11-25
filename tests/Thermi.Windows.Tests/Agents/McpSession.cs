using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Thermi.Windows.Agents;

namespace Thermi.Windows.Tests.Agents;

/// <summary>
/// The MCP SDK's own client talking to thermi's server over in-memory pipes, on the rig's settings and service, with the
/// rig's clock stepped as the calls run.
/// </summary>
internal sealed class McpSession(McpClient client, CancellationToken stop)
{
    public McpClient Client => client;

    /// <summary>Serves a session to <paramref name="work"/>, and ends it, the server's work and the clock's with it, once that is done.</summary>
    public static async Task RunAsync(AgentRig rig, Func<McpSession, Task> work)
    {
        using var stop = new CancellationTokenSource();
        using var toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        using var serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
        using var fromServer = new AnonymousPipeServerStream(PipeDirection.In);
        using var serverOut = new AnonymousPipeClientStream(PipeDirection.Out, fromServer.ClientSafePipeHandle);
        using var commands = new AgentCommands(rig.Environment, rig.Folder.Clock);
        var served = McpTools.ServeAsync(commands, serverIn, serverOut, NullLoggerFactory.Instance, stop.Token);
        var ticking = Tick(rig, stop.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(toServer, fromServer), cancellationToken: stop.Token);
            await work(new McpSession(client, stop.Token));
        }
        finally
        {
            await stop.CancelAsync();
            await ((Task)served).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await ticking.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>A tool's answer: whether it is an error, and its text, the command's JSON or why.</summary>
    public async Task<(bool Error, string Text)> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? [], cancellationToken: stop);
        return (result.IsError == true, string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
    }

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    public static JsonObject Answer(string text) => JsonNode.Parse(text)!.AsObject();

    // The settings file's waits and the first snapshot's are on the fake clock, which a person's seconds stand in for here.
    private static async Task Tick(AgentRig rig, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            rig.Folder.Clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromMilliseconds(5), TimeProvider.System, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
