using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Thermi.SensorHost;

internal interface ISensorRequest;

/// <summary>Serve readings: as the Windows service, or with <c>--console</c> in a terminal.</summary>
internal sealed record SensorServe(bool Console) : ISensorRequest;

/// <summary><c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second.</summary>
internal sealed record SensorDump(string File, int? Seconds) : ISensorRequest;

/// <summary><c>--frames &lt;file&gt; --seconds &lt;n&gt;</c>: every present of every process for a while, as ids and times.</summary>
internal sealed record SensorFrames(string File, int Seconds) : ISensorRequest;

/// <summary><c>--elevated</c>: the <c>thermi-elevated</c> service, which runs one of the window's fixes as SYSTEM.</summary>
internal sealed record SensorElevated : ISensorRequest;

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
        var frames = new Option<string>("--frames")
        {
            Description = "With --seconds, records every present of every process, as ids and times, as administrator.",
        };
        var seconds = new Option<int?>("--seconds")
        {
            Description = "With --dump, records a series of one snapshot a second; with --frames, how long it records.",
            CustomParser = Seconds,
        };
        var elevated = new Option<bool>("--elevated") { Description = "Runs the thermi-elevated service, which the service manager starts.", Hidden = true };
        var root = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.") { console, dump, frames, seconds, elevated };
        root.Validators.Add(result => Validate(result, (console, dump, frames, seconds, elevated)));

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

        if (parsed.GetValue(frames) is { } recording)
        {
            return new SensorFrames(recording, parsed.GetValue(seconds)!.Value);
        }

        return parsed.GetValue(dump) is { } file ? new SensorDump(file, parsed.GetValue(seconds)) : new SensorServe(parsed.GetValue(console));
    }

    private static void Validate(CommandResult result, (Option Console, Option Dump, Option Frames, Option Seconds, Option Elevated) options)
    {
        var (console, dump, frames, seconds, elevated) = options;
        if (result.GetResult(seconds) is not null && result.GetResult(dump) is null && result.GetResult(frames) is null)
        {
            result.AddError("--seconds needs --dump or --frames.");
        }

        if (result.GetResult(frames) is not null && (result.GetResult(seconds) is null || result.GetResult(dump) is not null))
        {
            result.AddError("--frames needs --seconds, and goes without --dump.");
        }

        if (result.GetResult(elevated) is not null
            && (result.GetResult(console) ?? result.GetResult(dump) ?? result.GetResult(frames) ?? (SymbolResult?)result.GetResult(seconds)) is not null)
        {
            result.AddError("--elevated takes no other option.");
        }
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
