using System.CommandLine;
using Thermi.Cli.Commands;

namespace Thermi.Windows.Commands;

/// <summary>
/// <c>thermi</c>'s command line on Windows: every OS's, <see cref="ThermiCommandLine"/>, with Windows' own command, parsed
/// before anything else runs, the overlay's single-instance mutex included.
/// </summary>
internal static class ThermiCommands
{
    /// <summary>The console door's name, the apphost every shell waits for, beside the windowed <c>thermi.exe</c>.</summary>
    public const string ConsoleDoor = "thermi-cli";

    public static bool IsConsoleDoor(string? processPath) =>
        string.Equals(Path.GetFileNameWithoutExtension(processPath), ConsoleDoor, StringComparison.OrdinalIgnoreCase);

    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static IThermiRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var verb = new Argument<string>("verb").AcceptOnlyFromAmong([.. Elevated.Verbs]);
        var elevated = ElevatedCommand(verb);
        var own = new Dictionary<Command, Func<ParseResult, IThermiRequest>>
        {
            [elevated] = it => new ElevatedRequest(it.GetValue(verb)!),
        };
        return ThermiCommandLine.Parse(args, own, parsed => OnlyItsVerb(parsed, elevated, args), out errors);
    }

    // The mode thermi starts behind a UAC prompt, with its verb and nothing else (docs/adr/0010-driver-and-service-from-the-window.md).
    private static Command ElevatedCommand(Argument<string> verb)
    {
        var elevated = new Command(Elevated.Command, "Starts the sensor service or installs the sensor driver, as administrator.") { verb };
        elevated.Hidden = true;
        return elevated;
    }

    // The options every command takes, --trace and --pseudo, and help, are refused there too.
    private static IEnumerable<string> OnlyItsVerb(ParseResult parsed, Command elevated, IReadOnlyList<string> args) =>
        parsed.CommandResult.Command == elevated && args.Count != 2 ? ["elevated takes its verb and nothing else."] : [];
}
