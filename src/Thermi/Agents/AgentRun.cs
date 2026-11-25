using Microsoft.Extensions.Logging;
using Thermi.Commands;

namespace Thermi.Agents;

/// <summary>A command's standard streams: its JSON goes to <see cref="Out"/>, why it did not finish to <see cref="Error"/>.</summary>
internal sealed record AgentConsole(Stream In, Stream Out, TextWriter Error);

/// <summary>
/// An agent's command as a process runs it: one JSON document on stdout, or a line each second for watch, or the MCP
/// server on stdin and stdout; why it did not finish on stderr; and its <see cref="AgentExit"/> as the exit code.
/// </summary>
internal static partial class AgentRun
{
    public static async Task<int> RunAsync(AgentRequest request, AgentEnvironment environment, AgentConsole console, ILoggerFactory logs, TimeProvider time)
    {
        var commands = new AgentCommands(environment, time);
        using var stop = new CancellationTokenSource();
        var output = new StreamWriter(console.Out, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        await using (output)
        {
            var reply = request.Verb == AgentVerb.Mcp ? new AgentReply((AgentExit)await McpTools.ServeAsync(commands, console.In, console.Out, logs, stop.Token))
                : request.Verb == AgentVerb.Watch ? await WatchAsync(commands, request, console.In, output, stop)
                : await commands.RunAsync(request, stop.Token);
            if (reply.Json is { } json)
            {
                await output.WriteLineAsync(json);
            }

            if (reply.Error is { } error)
            {
                await console.Error.WriteLineAsync(error);
                await console.Error.FlushAsync();
            }

            if (reply.Exit != AgentExit.Done)
            {
                Ended(environment.Log, request.Verb, reply.Exit, reply.Error);
            }

            return (int)reply.Exit;
        }
    }

    // A watch ends when whoever reads it closes its stdin, as an agent's tool does when it stops listening.
    private static async Task<AgentReply> WatchAsync(AgentCommands commands, AgentRequest request, Stream input, StreamWriter output, CancellationTokenSource stop)
    {
        var closed = Task.Run(
            async () =>
            {
                await input.CopyToAsync(Stream.Null, stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await stop.CancelAsync();
            },
            stop.Token);
        var reply = await commands.WatchAsync(request.Ids, output.WriteLineAsync, stop.Token);
        await stop.CancelAsync();
        await closed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return reply;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "An agent's {Verb} ended with {Exit}: {Error}")]
    private static partial void Ended(ILogger log, AgentVerb verb, AgentExit exit, string? error);
}
