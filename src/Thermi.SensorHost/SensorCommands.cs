using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Thermi.Core.Updates;

namespace Thermi.SensorHost;

internal interface ISensorRequest;

/// <summary>
/// Serve readings: as the Windows service, with <c>--console</c> in a terminal, or with <c>--user</c> as the user, when the
/// service is not installed (docs/adr/0016-admin-on-demand.md).
/// </summary>
internal sealed record SensorServe(bool Console, bool User = false) : ISensorRequest;

/// <summary><c>--dump &lt;file&gt;</c>, one snapshot, or with <c>--seconds &lt;n&gt;</c> a series of one a second.</summary>
internal sealed record SensorDump(string File, int? Seconds) : ISensorRequest;

/// <summary><c>--frames &lt;file&gt; --seconds &lt;n&gt;</c>: every present of every process for a while, as ids and times.</summary>
internal sealed record SensorFrames(string File, int Seconds) : ISensorRequest;

/// <summary><c>--elevated</c>: the <c>thermi-elevated</c> service, which runs one of the window's fixes as SYSTEM.</summary>
internal sealed record SensorElevated : ISensorRequest;

/// <summary>
/// <c>--switch &lt;version&gt;</c>: the watchdog of a switch to that version, which the service starts (docs/adr/0008-updates.md),
/// or with <c>--user</c> the user's own host (docs/adr/0016-admin-on-demand.md).
/// </summary>
internal sealed record SensorSwitch(ReleaseVersion To, bool User = false) : ISensorRequest;

/// <summary>
/// <c>--adopt</c>, with the MSI's product code when the MSI runs it: lays this version down as the one the PC runs;
/// <c>--undo</c>, which puts back what the last one changed; <c>--forget</c>, which drops that record once the install
/// committed; or <c>--remove</c>, which takes thermi off the PC
/// (docs/adr/0008-updates.md). With <c>--user</c>, a per-user install's (docs/adr/0016-admin-on-demand.md).
/// </summary>
internal sealed record SensorInstall(InstallMode Mode, string? Product, bool User = false) : ISensorRequest;

internal enum InstallMode
{
    Adopt,
    Undo,
    Forget,
    Remove,
}

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record SensorBuiltIn(ParseResult Parsed) : ISensorRequest;

/// <summary><c>thermi-sensors</c>' command line, parsed before the sensors are opened.</summary>
internal static class SensorCommands
{
    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static ISensorRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var options = new Options();
        var root = new RootCommand("thermi-sensors: the service that reads the sensors for thermi.")
        {
            options.Console, options.Dump, options.Frames, options.Seconds, options.Elevated, options.Watch, options.Adopt, options.Product,
            options.Undo, options.Forget, options.Remove, options.User,
        };
        root.Validators.Add(result => Validate(result, root, options));
        var parsed = root.Parse(args);
        errors = [.. parsed.Errors.Select(error => error.Message)];
        if (errors.Count > 0)
        {
            return null;
        }

        return parsed.Action is not null ? new SensorBuiltIn(parsed) : options.Request(parsed);
    }

    private static void Validate(CommandResult result, RootCommand root, Options options)
    {
        if (result.GetResult(options.Seconds) is not null && result.GetResult(options.Dump) is null && result.GetResult(options.Frames) is null)
        {
            result.AddError("--seconds needs --dump or --frames.");
        }

        if (result.GetResult(options.Frames) is not null && (result.GetResult(options.Seconds) is null || result.GetResult(options.Dump) is not null))
        {
            result.AddError("--frames needs --seconds, and goes without --dump.");
        }

        if (result.GetResult(options.Product) is not null && result.GetResult(options.Adopt) is null)
        {
            result.AddError("--product needs --adopt.");
        }

        foreach (var mode in new Option[] { options.Elevated, options.Watch, options.Adopt, options.Undo, options.Forget, options.Remove, options.User })
        {
            Alone(result, mode, [.. root.Options.Where(other => other != mode && !options.Goes(mode, other))]);
        }
    }

    // The service manager's own modes take nothing else.
    private static void Alone(CommandResult result, Option option, IEnumerable<Option> others)
    {
        if (result.GetResult(option) is not null && others.Any(other => result.GetResult(other) is not null))
        {
            result.AddError($"{option.Name} takes no other option.");
        }
    }

    private sealed class Options
    {
        public Option<bool> Console { get; } = new("--console") { Description = "Serves readings in this terminal, as administrator." };

        public Option<string> Dump { get; } = new("--dump") { Description = "Saves what the sensors read now to a file, for a bug report or test data." };

        public Option<string> Frames { get; } = new("--frames")
        {
            Description = "With --seconds, records every present of every process, as ids and times, as administrator.",
        };

        public Option<int?> Seconds { get; } = new("--seconds")
        {
            Description = "With --dump, records a series of one snapshot a second; with --frames, how long it records.",
            CustomParser = SecondsOf,
        };

        public Option<bool> Elevated { get; } = new("--elevated")
        {
            Description = "Runs the thermi-elevated service, which the service manager starts.",
            Hidden = true,
        };

        public Option<ReleaseVersion?> Watch { get; } = new("--switch")
        {
            Description = "Switches the service to a version.",
            Hidden = true,
            CustomParser = To,
        };

        public Option<bool> Adopt { get; } = new("--adopt") { Description = "Installs this version as the one the PC runs.", Hidden = true };

        public Option<Guid?> Product { get; } = new("--product") { Description = "With --adopt, the MSI that installed it.", Hidden = true };

        public Option<bool> Undo { get; } = new("--undo") { Description = "Puts back what the last install changed.", Hidden = true };

        public Option<bool> Forget { get; } = new("--forget") { Description = "Drops what the last install recorded, once it committed.", Hidden = true };

        public Option<bool> Remove { get; } = new("--remove") { Description = "Takes thermi off the PC.", Hidden = true };

        public Option<bool> User { get; } = new("--user") { Description = "Serves readings as this user, which the overlay starts.", Hidden = true };

        /// <summary>Whether <paramref name="other"/> may go with the mode <paramref name="mode"/>: an MSI's product, or a switch as the user.</summary>
        public bool Goes(Option mode, Option other) =>
            (mode == Adopt && other == Product)
            || (other == User && (mode == Watch || mode == Adopt || mode == Undo || mode == Forget || mode == Remove))
            || (mode == User && (other == Watch || other == Adopt || other == Product || other == Undo || other == Forget || other == Remove));

        public ISensorRequest Request(ParseResult parsed) =>
            parsed.GetValue(Elevated) ? new SensorElevated()
            : parsed.GetValue(Watch) is { } to ? new SensorSwitch(to, parsed.GetValue(User))
            : InstallOf(parsed) is { } install ? install
            : parsed.GetValue(Frames) is { } recording ? new SensorFrames(recording, parsed.GetValue(Seconds)!.Value)
            : parsed.GetValue(Dump) is { } file ? new SensorDump(file, parsed.GetValue(Seconds))
            : new SensorServe(parsed.GetValue(Console), parsed.GetValue(User));

        private SensorInstall? InstallOf(ParseResult parsed) =>
            parsed.GetValue(Adopt) ? new SensorInstall(InstallMode.Adopt, parsed.GetValue(Product)?.ToString("B").ToUpperInvariant(), parsed.GetValue(User))
            : parsed.GetValue(Undo) ? new SensorInstall(InstallMode.Undo, null, parsed.GetValue(User))
            : parsed.GetValue(Forget) ? new SensorInstall(InstallMode.Forget, null, parsed.GetValue(User))
            : parsed.GetValue(Remove) ? new SensorInstall(InstallMode.Remove, null, parsed.GetValue(User))
            : null;

        private static ReleaseVersion? To(ArgumentResult result)
        {
            var version = ReleaseVersion.Parse(result.Tokens.SingleOrDefault()?.Value);
            if (version is null)
            {
                result.AddError("--switch needs a version.");
            }

            return version;
        }

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
