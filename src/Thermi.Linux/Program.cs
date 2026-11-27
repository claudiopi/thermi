using System.CommandLine;
using System.Runtime.Versioning;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.Linux.Agents;
using Thermi.Linux.Commands;
using Thermi.Linux.Overlay;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux;

/// <summary>
/// <c>thermi</c> on Linux: the overlay, <c>thermi settings</c>, <c>thermi preview</c>, <c>thermi cpu-load</c> and the agents' commands; and
/// <c>thermi-cli</c>, the same program under another name, which runs the agents' commands alone. Arguments that make no
/// sense say why on stderr and exit with 2, before anything else runs; a command it does not run yet says so and exits 1.
/// </summary>
internal static class Program
{
    /// <summary>The name of the apphost that runs the agents' commands alone, beside <c>thermi</c>.</summary>
    public const string ConsoleDoor = "thermi-cli";

    private static async Task<int> Main(string[] args)
    {
        var request = Parse(args, out var errors);
        if (request is null)
        {
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync(error);
            }

            return 2;
        }

        var time = TimeProvider.System;
        var door = Path.GetFileName(Environment.ProcessPath) == ConsoleDoor;
        return request switch
        {
            AgentRequest agent => await RunAgentAsync(agent, time),
            // The console door's stdout carries JSON alone, so its help and version go to stderr.
            BuiltInRequest builtIn => await builtIn.Parsed.InvokeAsync(new InvocationConfiguration { Output = door ? Console.Error : Console.Out }),
            // The fan finder's load, which the sensors' package alone runs through thermi-cli, as it has no thermi.
            CpuLoadRequest cpu => await CpuLoad.RunAsync(cpu.Seconds, time),
            _ when door => await HelpAsync(),
            PreviewRequest preview => await Preview.RunAsync(Painted(preview), time),
            OverlayRequest overlay => await OverlayStart.RunAsync(Painted(overlay), time),
            SettingsRequest settings => await Window.WindowProcess.RunAsync(Painted(settings), args, time),
            _ => await NotHereAsync(),
        };
    }

    private static IThermiRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors) =>
        ThermiCommandLine.Parse(args, new Dictionary<Command, Func<ParseResult, IThermiRequest>>(), _ => [], out errors);

    // GirCore binds each library when its module starts, and nothing of theirs runs before; only what paints loads them,
    // so the agents' commands run on a server with none.
    private static T Painted<T>(T request)
    {
        Gio.Module.Initialize();
        PangoCairo.Module.Initialize();
        Gdk.Module.Initialize();
        return request;
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
    private static async Task<int> HelpAsync()
    {
        if (Parse(["--help"], out _) is BuiltInRequest help)
        {
            await help.Parsed.InvokeAsync(new InvocationConfiguration { Output = Console.Error });
        }

        return 2;
    }

    private static async Task<int> NotHereAsync()
    {
        await Console.Error.WriteLineAsync("thermi: this build does not run that command on Linux yet.");
        return 1;
    }
}
