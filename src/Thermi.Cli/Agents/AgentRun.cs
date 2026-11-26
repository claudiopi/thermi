using Microsoft.Extensions.Logging;
using Thermi.Cli.Commands;

namespace Thermi.Cli.Agents;

/// <summary>A command's standard streams: its JSON goes to <see cref="Out"/>, why it did not finish to <see cref="Error"/>.</summary>
internal sealed record AgentConsole(Stream In, Stream Out, TextWriter Error);

/// <summary>
/// An agent's command as a process runs it: one JSON document on stdout, or a line each second for watch, or the MCP
/// server on stdin and stdout; why it did not finish on stderr; and its <see cref="AgentExit"/> as the exit code.
/// </summary>
internal static partial class AgentRun
{
    // An elevated write into the user's folders, which a link there can send anywhere, is one thermi never makes.
    public const string RaisedRefusal =
        "thermi-cli was started elevated, so it reads but changes nothing: run a command that writes from an unelevated terminal.";

    public static async Task<int> RunAsync(AgentRequest request, IAgentEnvironment environment, AgentConsole console, ILoggerFactory logs, TimeProvider time)
    {
        if (request.Writes && environment.Raised())
        {
            await console.Error.WriteLineAsync(RaisedRefusal);
            return (int)AgentExit.Refused;
        }

        using var commands = new AgentCommands(environment, time);
        using var stop = new CancellationTokenSource();
        var output = new StreamWriter(console.Out, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        await using (output)
        {
            var reply = request.Verb == AgentVerb.Mcp
                ? new AgentReply((AgentExit)await McpTools.ServeAsync(commands, console.In, console.Out, logs, stop.Token))
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

    // A watch ends when whoever reads it closes its stdin, as an agent's tool does when it stops listening. One that ends
    // by itself, as when the service goes quiet, does not wait for stdin: Windows cannot cancel a read of it once begun.
    private static Task<AgentReply> WatchAsync(
        AgentCommands commands, AgentRequest request, Stream input, StreamWriter output, CancellationTokenSource stop)
    {
        var token = stop.Token;
        _ = Task.Run(
            async () =>
            {
                await input.CopyToAsync(Stream.Null, token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                try
                {
                    await stop.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                    // The watch ended first, and the process with it.
                }
            },
            CancellationToken.None);
        return commands.WatchAsync(request.Ids, output.WriteLineAsync, token);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "An agent's {Verb} ended with {Exit}: {Error}")]
    private static partial void Ended(ILogger log, AgentVerb verb, AgentExit exit, string? error);
}
