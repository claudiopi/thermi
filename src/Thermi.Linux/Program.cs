using System.CommandLine;
using System.Runtime.Versioning;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.Linux.Agents;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux;

/// <summary>
/// <c>thermi</c> on Linux: the overlay, <c>thermi settings</c>, <c>thermi preview</c>, <c>thermi cpu-load</c> and the
/// agents' commands; and <c>thermi-cli</c>, the same program under another name, which runs the agents' commands alone.
/// Arguments that make no sense say why on stderr and exit with 2, before anything else runs.
/// </summary>
internal static class Program
{
    /// <summary>The name of the apphost that runs the agents' commands alone, beside <c>thermi</c>.</summary>
    public const string ConsoleDoor = "thermi-cli";

    private static async Task<int> Main(string[] args)
    {
        var time = TimeProvider.System;
        var (cpuLoad, load) = CpuLoadCommand.Create();
        var root = new RootCommand("thermi: a light, always-on-top hardware monitor.") { cpuLoad };
        var request = AgentCommandLine.Parse(root, new() { [cpuLoad] = load }, args, _ => [], _ => null, out var errors);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync(error);
            }

            return 2;
        }

        var door = Path.GetFileName(Environment.ProcessPath) == ConsoleDoor;
        return request switch
        {
            AgentRequest agent => await RunAgentAsync(agent, time),
            // The console door's stdout carries JSON alone, so its help and version go to stderr.
            BuiltInRequest builtIn => await builtIn.Parsed.InvokeAsync(new InvocationConfiguration { Output = door ? Console.Error : Console.Out }),
            CpuLoadRequest cpu when !door => await CpuLoad.RunAsync(cpu.Seconds, time),
            _ when door => await HelpAsync(root),
            _ => await NoOverlayAsync(),
        };
    }

    private static async Task<int> RunAgentAsync(AgentRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.AgentsPath, time, trace: false);
        var environment = new LinuxAgentEnvironment(logs.CreateLogger("Thermi.Agents"), time);
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        return await ThermiLog.RunLoggedAsync(
            environment.Log,
            () => AgentRun.RunAsync(request, environment, new AgentConsole(input, output, Console.Error), logs, time));
    }

    // The console door runs no overlay: with no command, it says what it does, as a mistyped command does, and exits with 2.
    private static async Task<int> HelpAsync(RootCommand root)
    {
        await root.Parse(["--help"]).InvokeAsync(new InvocationConfiguration { Output = Console.Error });
        return 2;
    }

    private static async Task<int> NoOverlayAsync()
    {
        await Console.Error.WriteLineAsync("thermi: this build shows no overlay.");
        return 1;
    }
}
