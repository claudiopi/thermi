using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Thermi.SensorHost;

internal interface ISensorRequest;

/// <summary>Serve readings: as the Windows service, or with <c>--console</c> in a terminal.</summary>
internal sealed record SensorServe(bool Console) : ISensorRequest;

/// <summary><c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second.</summary>
internal sealed record SensorDump(string File, int? Seconds) : ISensorRequest;

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record SensorBuiltIn(ParseResult Parsed) : ISensorRequest;

/// <summary><c>thermi-sensors</c>' command line, parsed before the sensors are opened.</summary>
internal static class SensorCommands
{
    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static ISensorRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var console = new Option<bool>("--console") { Description = "Serves readings in this terminal, as administrator." };
        var dump = new Option<string>("--dump") { Description = "Saves what the sensors read now to a file, for a bug report or test data." };
        var seconds = new Option<int?>("--seconds") { Description = "With --dump, records a series of one snapshot a second.", CustomParser = Seconds };
        var root = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.") { console, dump, seconds };
        root.Validators.Add(result =>
        {
            if (result.GetResult(seconds) is not null && result.GetResult(dump) is null)
            {
                result.AddError("--seconds needs --dump.");
            }
        });

        var parsed = root.Parse(args);
        errors = [.. parsed.Errors.Select(error => error.Message)];
        if (errors.Count > 0)
        {
            return null;
        }

        if (parsed.Action is not null)
        {
            return new SensorBuiltIn(parsed);
        }

        return parsed.GetValue(dump) is { } file ? new SensorDump(file, parsed.GetValue(seconds)) : new SensorServe(parsed.GetValue(console));
    }

    private static int? Seconds(ArgumentResult result)
    {
        if (result.Tokens is [var token] && int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        result.AddError("--seconds needs a whole number above zero.");
        return null;
    }
}
