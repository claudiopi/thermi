using System.CommandLine;
using System.CommandLine.Parsing;
using Thermi.Core;

namespace Thermi.Sensors.MacOS.Serving;

internal interface ISensorRequest;

/// <summary>
/// Serve readings: as launchd's agent, on the socket launchd hands it, or with <c>--console</c> in a terminal, on the
/// person's socket, or the one <c>THERMI_SENSORS_SOCKET</c> names; with <c>--replay</c>, from a recording in place of the Mac.
/// </summary>
internal sealed record SensorServe(bool Console, string? Replay) : ISensorRequest;

/// <summary>
/// <c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second; with
/// <c>--raw &lt;folder&gt;</c>, what the Mac answered too, for tests to replay; either from <c>--replay</c>.
/// </summary>
internal sealed record SensorRecord(string? Dump, string? Raw, int Seconds, string? Replay) : ISensorRequest;

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record SensorBuiltIn(ParseResult Parsed) : ISensorRequest;

/// <summary><c>thermi-sensors</c>' command line on macOS, parsed before the sensors are opened.</summary>
internal static class SensorCommands
{
    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static ISensorRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var options = new Options();
        var command = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.")
        {
            options.Console, options.Dump, options.Raw, options.Seconds, options.Replay,
        };
        command.Validators.Add(options.Validate);
        var parsed = command.Parse(args);
        errors = [.. parsed.Errors.Select(error => error.Message)];
        return errors.Count > 0 ? null : parsed.Action is not null ? new SensorBuiltIn(parsed) : options.Request(parsed);
    }

    private sealed class Options
    {
        public Option<bool> Console { get; } = new("--console") { Description = "Serves readings in this terminal, on your own socket." };

        public Option<string> Dump { get; } = new("--dump") { Description = "Saves what the sensors read now to a file, for a bug report or test data." };

        public Option<string> Raw { get; } = new("--raw")
        {
            Description = "Saves what the SMC, the HID sensors, IOReport and IOKit answered, for tests to replay.",
        };

        public Option<int?> Seconds { get; } = new("--seconds")
        {
            Description = "With --dump or --raw, records a series of one snapshot a second.",
            CustomParser = SecondsOf,
        };

        public Option<string> Replay { get; } = new("--replay") { Description = "Reads a recording --raw saved in place of the Mac." };

        public void Validate(CommandResult result)
        {
            var records = result.GetResult(Dump) is not null || result.GetResult(Raw) is not null;
            if (result.GetResult(Seconds) is not null && !records)
            {
                result.AddError("--seconds needs --dump or --raw.");
            }

            if (result.GetResult(Console) is not null && records)
            {
                result.AddError("--console goes without --dump and --raw.");
            }
        }

        public ISensorRequest Request(ParseResult parsed) =>
            parsed.GetValue(Dump) is not null || parsed.GetValue(Raw) is not null
                ? new SensorRecord(parsed.GetValue(Dump), parsed.GetValue(Raw), parsed.GetValue(Seconds) ?? 1, parsed.GetValue(Replay))
                : new SensorServe(parsed.GetValue(Console), parsed.GetValue(Replay));

        private static int? SecondsOf(ArgumentResult result)
        {
            var seconds = WholeNumber.Parse(result.Tokens.SingleOrDefault()?.Value, 1, int.MaxValue);
            if (seconds is null)
            {
                result.AddError(WholeNumber.Expected("--seconds", 1));
            }

            return seconds;
        }
    }
}
