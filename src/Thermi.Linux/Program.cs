using System.CommandLine;
using System.Runtime.Versioning;
using Thermi.Cli.Commands;
using Thermi.Linux.Commands;

[assembly: SupportedOSPlatform("linux")]

namespace Thermi.Linux;

/// <summary><c>thermi</c> on Linux: the overlay, <c>thermi settings</c> and <c>thermi preview</c>.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var request = ThermiCommandLine.Parse(args, new Dictionary<Command, Func<ParseResult, IThermiRequest>>(), _ => [], out var errors);
        if (request is null)
        {
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync(error);
            }

            return 2;
        }

        // GirCore binds each library when its module starts, and nothing of theirs runs before.
        Gio.Module.Initialize();
        PangoCairo.Module.Initialize();
        Gdk.Module.Initialize();
        var time = TimeProvider.System;
        return request switch
        {
            BuiltInRequest builtIn => await builtIn.Parsed.InvokeAsync(),
            PreviewRequest preview => await Preview.RunAsync(preview, time),
            SettingsRequest settings => await Window.WindowProcess.RunAsync(settings, args, new Window.QuietFeed(), time),
            _ => await NotHereAsync(),
        };
    }

    private static async Task<int> NotHereAsync()
    {
        await Console.Error.WriteLineAsync("thermi: this build does not run that command on Linux yet.");
        return 1;
    }
}
