using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Thermi.Sensors.Linux.Serving;

internal interface ISensorRequest;

/// <summary>
/// Serve readings: as systemd's service, on the socket systemd hands it, or with <c>--console</c> in a terminal, on a socket
/// of its own in the user's runtime folder; with <c>--root</c>, from a recorded tree in place of the kernel's files.
/// </summary>
internal sealed record SensorServe(bool Console, string? Root) : ISensorRequest;

/// <summary>
/// <c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second; with
/// <c>--tree &lt;folder&gt;</c>, the files the kernel answered with too, for tests to replay; either from <c>--root</c>.
/// </summary>
internal sealed record SensorRecord(string? Dump, string? Tree, int Seconds, string? Root) : ISensorRequest;

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record SensorBuiltIn(ParseResult Parsed) : ISensorRequest;

/// <summary><c>thermi-sensors</c>' command line on Linux, parsed before the sensors are opened.</summary>
internal static class SensorCommands
{
    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static ISensorRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var options = new Options();
        var command = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.")
        {
            options.Console, options.Dump, options.Tree, options.Seconds, options.Root,
        };
        command.Validators.Add(options.Validate);
        var parsed = command.Parse(args);
        errors = [.. parsed.Errors.Select(error => error.Message)];
        return errors.Count > 0 ? null : parsed.Action is not null ? new SensorBuiltIn(parsed) : options.Request(parsed);
    }

    private sealed class Options
    {
        public Option<bool> Console { get; } = new("--console") { Description = "Serves readings in this terminal, on a socket of your own." };

        public Option<string> Dump { get; } = new("--dump") { Description = "Saves what the sensors read now to a file, for a bug report or test data." };

        public Option<string> Tree { get; } = new("--tree") { Description = "Saves the files under /sys and /proc the sensors read, for tests to replay." };

        public Option<int?> Seconds { get; } = new("--seconds")
        {
            Description = "With --dump or --tree, records a series of one snapshot a second.",
            CustomParser = SecondsOf,
        };

        public Option<string> Root { get; } = new("--root") { Description = "Reads a tree --tree saved in place of the kernel's files." };

        public void Validate(CommandResult result)
        {
            var records = result.GetResult(Dump) is not null || result.GetResult(Tree) is not null;
            if (result.GetResult(Seconds) is not null && !records)
            {
                result.AddError("--seconds needs --dump or --tree.");
            }

            if (result.GetResult(Console) is not null && records)
            {
                result.AddError("--console goes without --dump and --tree.");
            }

            if (result.GetResult(Root) is not null && result.GetResult(Console) is null && !records)
            {
                result.AddError("--root needs --console, --dump or --tree.");
            }
        }

        public ISensorRequest Request(ParseResult parsed) =>
            parsed.GetValue(Dump) is not null || parsed.GetValue(Tree) is not null
                ? new SensorRecord(parsed.GetValue(Dump), parsed.GetValue(Tree), parsed.GetValue(Seconds) ?? 1, parsed.GetValue(Root))
                : new SensorServe(parsed.GetValue(Console), parsed.GetValue(Root));

        private static int? SecondsOf(ArgumentResult result)
        {
            if (result.Tokens is [var token] && int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }

            result.AddError("--seconds needs a whole number above zero.");
            return null;
        }
    }
}
