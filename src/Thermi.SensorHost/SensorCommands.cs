using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Thermi.Core.Updates;

namespace Thermi.SensorHost;

internal interface ISensorRequest;

/// <summary>Serve readings: as the Windows service, or with <c>--console</c> in a terminal.</summary>
internal sealed record SensorServe(bool Console) : ISensorRequest;

/// <summary><c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second.</summary>
internal sealed record SensorDump(string File, int? Seconds) : ISensorRequest;

/// <summary><c>--elevated</c>: the <c>thermi-elevated</c> service, which runs one of the window's fixes as SYSTEM.</summary>
internal sealed record SensorElevated : ISensorRequest;

/// <summary><c>--switch &lt;version&gt;</c>: the watchdog of a switch to that version, which the service starts (docs/adr/0008-updates.md).</summary>
internal sealed record SensorSwitch(ReleaseVersion To) : ISensorRequest;

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
        var elevated = new Option<bool>("--elevated") { Description = "Runs the thermi-elevated service, which the service manager starts.", Hidden = true };
        var watch = new Option<ReleaseVersion?>("--switch") { Description = "Switches the service to a version.", Hidden = true, CustomParser = To };
        var root = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.") { console, dump, seconds, elevated, watch };
        root.Validators.Add(result =>
        {
            if (result.GetResult(seconds) is not null && result.GetResult(dump) is null)
            {
                result.AddError("--seconds needs --dump.");
            }

            Alone(result, elevated, console, dump, seconds, watch);
            Alone(result, watch, console, dump, seconds, elevated);
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

        if (parsed.GetValue(elevated))
        {
            return new SensorElevated();
        }

        if (parsed.GetValue(watch) is { } to)
        {
            return new SensorSwitch(to);
        }

        return parsed.GetValue(dump) is { } file ? new SensorDump(file, parsed.GetValue(seconds)) : new SensorServe(parsed.GetValue(console));
    }

    // The service manager's own modes take nothing else.
    private static void Alone(CommandResult result, Option option, params Option[] others)
    {
        if (result.GetResult(option) is not null && others.Any(other => result.GetResult(other) is not null))
        {
            result.AddError($"{option.Name} takes no other option.");
        }
    }

    private static ReleaseVersion? To(ArgumentResult result)
    {
        var version = ReleaseVersion.Parse(result.Tokens.SingleOrDefault()?.Value);
        if (version is null)
        {
            result.AddError("--switch needs a version.");
        }

        return version;
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
