using System.CommandLine;
using System.Runtime.Versioning;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.MacOS.Cli.Agents;

[assembly: SupportedOSPlatform("macos")]

namespace Thermi.MacOS.Cli;

/// <summary>
/// <c>thermi-cli</c> on macOS: the agents' commands and the MCP server, over the person's service's socket, and the fan
/// finder's load. Its standard output carries JSON alone, so its help and version go to stderr; arguments that make no
/// sense say why there and exit with 2, as a command only <c>thermi</c> runs does.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var request = ThermiCommandLine.Parse(args, new Dictionary<Command, Func<ParseResult, IThermiRequest>>(), _ => [], out var errors);
        foreach (var error in errors)
        {
            await Console.Error.WriteLineAsync(error);
        }

        var time = TimeProvider.System;
        return request switch
        {
            AgentRequest agent => await RunAgentAsync(agent, time),
            BuiltInRequest builtIn => await builtIn.Parsed.InvokeAsync(new InvocationConfiguration { Output = Console.Error }),
            CpuLoadRequest cpu => await CpuLoad.RunAsync(cpu.Seconds, time),
            null => 2,
            _ => await ThermiAloneAsync(),
        };
    }

    private static async Task<int> RunAgentAsync(AgentRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.AgentsPath, time, trace: false);
        var environment = new MacAgentEnvironment(logs.CreateLogger("Thermi.Agents"), time);
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await ThermiLog.RunLoggedAsync(
            environment.Log,
            () => AgentRun.RunAsync(request, environment, new AgentConsole(input, output, Console.Error), logs, time));
    }

    private static async Task<int> ThermiAloneAsync()
    {
        await Console.Error.WriteLineAsync("thermi-cli runs the agents' commands; thermi runs the overlay, its window and its preview.");
        return 2;
    }
}
