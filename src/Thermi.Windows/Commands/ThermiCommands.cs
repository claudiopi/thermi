using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Thermi.Cli.Commands;

namespace Thermi.Windows.Commands;

/// <summary>The overlay, with no command: what Windows starts at sign-in.</summary>
internal sealed record OverlayRequest(bool Trace) : IThermiRequest;

/// <summary>
/// <c>thermi settings [--overlay n] [--item id | --size]</c>: the window, on an overlay counted from 1, or 0 for none, and
/// an item to select or the overlay's Size row.
/// </summary>
internal sealed record SettingsRequest(int Overlay, bool Trace, bool Pseudo) : IThermiRequest
{
    public string? Item { get; init; }

    public bool SizeRow { get; init; }
}

/// <summary>
/// <c>thermi preview &lt;snapshot&gt; &lt;png&gt; [scale] [settings]</c>: the overlay painted from a saved snapshot, or the
/// last of a series with its graphs, never shown. With no snapshot, <c>preview</c> is the agents' picture of the readings now.
/// </summary>
internal sealed record PreviewRequest(string Snapshot, string Png, float Scale, string? SettingsPath) : IThermiRequest;

/// <summary><c>thermi cpu-load &lt;seconds&gt;</c>: every core loaded, to record how the fans answer.</summary>
internal sealed record CpuLoadRequest(int Seconds) : IThermiRequest;

/// <summary>
/// <c>thermi</c>'s command line, parsed before anything else runs, the overlay's single-instance mutex included. Numbers
/// are read in the invariant culture, so a scale of 1.5 means the same in every language.
/// </summary>
internal static class ThermiCommands
{
    public const string TraceOption = "--trace";

    public const string OverlayOption = "--overlay";

    public const string ItemOption = "--item";

    public const string SizeOption = "--size";

    public const int MaxLoadSeconds = 600;

    /// <summary>The console door's name, the apphost every shell waits for, beside the windowed <c>thermi.exe</c>.</summary>
    public const string ConsoleDoor = "thermi-cli";

    public static bool IsConsoleDoor(string? processPath) =>
        string.Equals(Path.GetFileNameWithoutExtension(processPath), ConsoleDoor, StringComparison.OrdinalIgnoreCase);

    /// <summary>The request, or null with <paramref name="errors"/> saying why the arguments make no sense.</summary>
    public static IThermiRequest? Parse(IReadOnlyList<string> args, out IReadOnlyList<string> errors)
    {
        var trace = new Option<bool>(TraceOption) { Recursive = true, Description = "Logs each paint, input and the window's first frame too." };

        // The window's texts made longer, to see what a long translation does to it.
        var pseudo = new Option<bool>("--pseudo") { Recursive = true, Hidden = true };
        var overlay = new Option<int>(OverlayOption) { Description = "The overlay to edit, counted from 1.", CustomParser = Whole(1, int.MaxValue) };
        var item = new Option<string?>(ItemOption) { Description = "The id of the item to select." };
        var size = new Option<bool>(SizeOption) { Description = "Goes to the overlay's Size row." };
        var settings = new Command("settings", "Opens the settings window.") { overlay, item, size };
        var preview = PreviewCommand(out var previewed);
        var seconds = new Argument<int>("seconds") { CustomParser = Whole(1, MaxLoadSeconds) };
        var cpuLoad = new Command("cpu-load", "Loads every core at idle priority, to record how the fans answer.") { seconds };
        var verb = new Argument<string>("verb").AcceptOnlyFromAmong([.. Elevated.Verbs]);
        var elevated = ElevatedCommand(verb);
        var root = new RootCommand("thermi: a light, always-on-top hardware monitor.") { trace, pseudo, settings, preview, cpuLoad, elevated };
        var requests = new Dictionary<Command, Func<ParseResult, IThermiRequest>>
        {
            [settings] = it => new SettingsRequest(it.GetValue(overlay), it.GetValue(trace), it.GetValue(pseudo))
            {
                Item = it.GetValue(item),
                SizeRow = it.GetValue(size),
            },
            [preview] = previewed,
            [cpuLoad] = it => new CpuLoadRequest(it.GetValue(seconds)),
            [elevated] = it => new ElevatedRequest(it.GetValue(verb)!),
        };
        return AgentCommandLine.Parse(
            root, requests, args, parsed => OnlyItsVerb(parsed, elevated, args), parsed => new OverlayRequest(parsed.GetValue(trace)), out errors);
    }

    // A saved snapshot's picture into a PNG file; with none named, the agents' picture of the readings now, whose options it
    // takes, so thermi and thermi-cli have one preview.
    private static Command PreviewCommand(out Func<ParseResult, IThermiRequest> request)
    {
        var snapshot = new Argument<FileInfo>("snapshot")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "A snapshot saved with thermi-sensors --dump, or a series saved with --seconds; the readings of now when none.",
        }.AcceptExistingOnly();
        var png = new Argument<string>("png") { Arity = ArgumentArity.ZeroOrOne, Description = "Where the picture goes." };
        var scale = new Argument<float>("scale") { Arity = ArgumentArity.ZeroOrOne, CustomParser = AgentCommandLine.Scale, DefaultValueFactory = _ => 1 };

        // A settings file that is not there paints the defaults, and is not created.
        var file = new Argument<string?>("settings") { Arity = ArgumentArity.ZeroOrOne, Description = "A settings file other than the user's." };
        var preview = new Command("preview", "Paints an overlay for a saved snapshot into a PNG, without showing it, or as it draws the readings now.")
        {
            snapshot, png, scale, file, AgentCommandLine.PreviewOverlay, AgentCommandLine.PreviewScale,
        };
        preview.Validators.Add(result => OfSaved(result, snapshot, png));
        request = it => it.GetValue(snapshot) is { } saved
            ? new PreviewRequest(saved.FullName, it.GetValue(png)!, it.GetValue(scale), it.GetValue(file))
            : AgentCommandLine.Previewed(it);
        return preview;
    }

    // A saved snapshot's picture goes into its png, at the scale after it; the options are those of the picture of now.
    private static void OfSaved(CommandResult result, Argument<FileInfo> snapshot, Argument<string> png)
    {
        if (result.GetResult(snapshot) is not { Tokens.Count: > 0 })
        {
            return;
        }

        if (result.GetResult(png) is not { Tokens.Count: > 0 })
        {
            result.AddError("A saved snapshot's picture needs the png it goes to.");
        }

        if (result.GetResult(AgentCommandLine.PreviewOverlay) is { Implicit: false } || result.GetResult(AgentCommandLine.PreviewScale) is { Implicit: false })
        {
            result.AddError("--overlay and --scale paint the readings of now; a saved snapshot's scale follows its png.");
        }
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
}
