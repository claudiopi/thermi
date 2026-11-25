using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Thermi.Commands;

/// <summary>What <c>thermi</c> was asked to do; null from <see cref="ThermiCommands.Parse"/>, with why, when its arguments make no sense.</summary>
internal interface IThermiRequest;

/// <summary>The overlay, with no command: what Windows starts at sign-in.</summary>
internal sealed record OverlayRequest(bool Trace) : IThermiRequest;

/// <summary><c>thermi settings [--overlay n] [--item id]</c>: the window, on an overlay counted from 1, or 0 for none, and an item to select.</summary>
internal sealed record SettingsRequest(int Overlay, bool Trace, bool Pseudo) : IThermiRequest
{
    public string? Item { get; init; }
}

/// <summary>
/// <c>thermi preview &lt;snapshot&gt; &lt;png&gt; [scale] [settings]</c>: the overlay painted from a saved snapshot, or the
/// last of a series with its graphs, never shown.
/// </summary>
internal sealed record PreviewRequest(string Snapshot, string Png, float Scale, string? SettingsPath) : IThermiRequest;

/// <summary><c>thermi cpu-load &lt;seconds&gt;</c>: every core loaded, to record how the fans answer.</summary>
internal sealed record CpuLoadRequest(int Seconds) : IThermiRequest;

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record BuiltInRequest(ParseResult Parsed) : IThermiRequest;

/// <summary>
/// <c>thermi</c>'s command line, parsed before anything else runs, the overlay's single-instance mutex included. Numbers
/// are read in the invariant culture, so a scale of 1.5 means the same in every language.
/// </summary>
internal static class ThermiCommands
{
    public const string TraceOption = "--trace";

    public const string OverlayOption = "--overlay";

    public const string ItemOption = "--item";

    public const int MaxLoadSeconds = 600;

    // A preview's bitmap grows with the square of its scale: past 8 it is 64 times the overlay's pixels for no reader's
    // gain, and under a quarter its text is a few pixels high.
    public const float MinScale = 0.25f;

    public const float MaxScale = 8;

    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static IThermiRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var trace = new Option<bool>(TraceOption) { Recursive = true, Description = "Logs each paint, input and the window's first frame too." };

        // The window's texts made longer, to see what a long translation does to it.
        var pseudo = new Option<bool>("--pseudo") { Recursive = true, Hidden = true };
        var overlay = new Option<int>(OverlayOption) { Description = "The overlay to edit, counted from 1.", CustomParser = Whole(1, int.MaxValue) };
        var item = new Option<string?>(ItemOption) { Description = "The id of the item to select." };
        var settings = new Command("settings", "Opens the settings window.") { overlay, item };
        var snapshot = new Argument<FileInfo>("snapshot")
        {
            Description = "A snapshot saved with thermi-sensors --dump, or a series saved with --seconds.",
        }.AcceptExistingOnly();
        var png = new Argument<string>("png") { Description = "Where the picture goes." };
        var scale = new Argument<float>("scale") { Arity = ArgumentArity.ZeroOrOne, CustomParser = Scale, DefaultValueFactory = _ => 1 };
        // A settings file that is not there paints the defaults, and is not created.
        var file = new Argument<string?>("settings") { Arity = ArgumentArity.ZeroOrOne, Description = "A settings file other than the user's." };
        var preview = new Command("preview", "Paints the overlay for a saved snapshot, without showing it.") { snapshot, png, scale, file };
        var seconds = new Argument<int>("seconds") { CustomParser = Whole(1, MaxLoadSeconds) };
        var cpuLoad = new Command("cpu-load", "Loads every core at idle priority, to record how the fans answer.") { seconds };
        var verb = new Argument<string>("verb").AcceptOnlyFromAmong([.. Elevated.Verbs]);
        var elevated = ElevatedCommand(verb);
        var root = new RootCommand("thermi: a light, always-on-top hardware monitor.") { trace, pseudo, settings, preview, cpuLoad, elevated };

        // A root with commands and no action of its own must be given one of them; thermi's runs the overlay.
        root.SetAction(_ => 0);
        var parsed = root.Parse(args);
        errors = [.. parsed.Errors.Select(error => error.Message), .. OnlyItsVerb(parsed, elevated, args)];
        if (errors.Count > 0)
        {
            return null;
        }

        var requests = new Dictionary<Command, Func<ParseResult, IThermiRequest>>
        {
            [settings] = it => new SettingsRequest(it.GetValue(overlay), it.GetValue(trace), it.GetValue(pseudo)) { Item = it.GetValue(item) },
            [preview] = it => new PreviewRequest(it.GetValue(snapshot)!.FullName, it.GetValue(png)!, it.GetValue(scale), it.GetValue(file)),
            [cpuLoad] = it => new CpuLoadRequest(it.GetValue(seconds)),
            [elevated] = it => new ElevatedRequest(it.GetValue(verb)!),
        };
        return parsed.Action is not null && parsed.Action != root.Action ? new BuiltInRequest(parsed)
            : requests.TryGetValue(parsed.CommandResult.Command, out var request) ? request(parsed) : new OverlayRequest(parsed.GetValue(trace));
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

    private static Func<ArgumentResult, int> Whole(int min, int max) => result =>
    {
        if (result.Tokens is [var token]
            && int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= min
            && value <= max)
        {
            return value;
        }

        var range = max == int.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"{min} up")
            : string.Create(CultureInfo.InvariantCulture, $"{min} to {max}");
        result.AddError(string.Create(CultureInfo.InvariantCulture, $"{result.Argument.Name} takes a whole number from {range}."));
        return 0;
    };

    private static float Scale(ArgumentResult result)
    {
        if (result.Tokens is [])
        {
            return 1;
        }

        // The range also leaves out NaN and the infinities, which every comparison with them refuses.
        if (result.Tokens is [var token]
            && float.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value >= MinScale
            && value <= MaxScale)
        {
            return value;
        }

        result.AddError(string.Create(CultureInfo.InvariantCulture, $"scale takes a number from {MinScale} to {MaxScale}, such as 1.5."));
        return 1;
    }
}
