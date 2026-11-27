using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace Thermi.Cli.Commands;

/// <summary>The overlay, with no command: what the desktop starts at sign-in.</summary>
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
/// last of a series with its graphs, never shown.
/// </summary>
internal sealed record PreviewRequest(string Snapshot, string Png, float Scale, string? SettingsPath) : IThermiRequest;

/// <summary>
/// <c>thermi</c>'s command line on every OS, parsed before anything else runs: the overlay, the settings window, the
/// preview, <c>cpu-load</c> and the agents' commands, and a host's own commands beside them. Numbers are read in the invariant culture,
/// so a scale of 1.5 means the same in every language.
/// </summary>
internal static class ThermiCommandLine
{
    public const string TraceOption = "--trace";

    public const string OverlayOption = "--overlay";

    public const string ItemOption = "--item";

    public const string SizeOption = "--size";

    // A preview's picture grows with the square of its scale: past 8 it is 64 times the overlay's pixels for no reader's
    // gain, and under a quarter its text is a few pixels high.
    public const float MinScale = 0.25f;

    public const float MaxScale = 8;

    /// <summary>
    /// The request, or null with <paramref name="errors"/> saying why the arguments make no sense, as System.CommandLine
    /// or <paramref name="refused"/> finds; <paramref name="own"/> are the host's commands and the requests they make.
    /// </summary>
    public static IThermiRequest? Parse(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<Command, Func<ParseResult, IThermiRequest>> own,
        Func<ParseResult, IEnumerable<string>> refused,
        out IReadOnlyList<string> errors)
    {
        var trace = new Option<bool>(TraceOption) { Recursive = true, Description = "Logs each paint, input and the window's first frame too." };

        var pseudo = new Option<bool>("--pseudo") { Recursive = true, Hidden = true };
        var root = new RootCommand("thermi: a light, always-on-top hardware monitor.") { trace, pseudo };
        var (cpuLoad, load) = CpuLoadCommand.Create();
        var requests = new Dictionary<Command, Func<ParseResult, IThermiRequest>>
        {
            [Settings(trace, pseudo, out var settings)] = settings,
            [Preview(out var preview)] = preview,
            [cpuLoad] = load,
        };
        foreach (var (command, request) in own)
        {
            requests[command] = request;
        }

        foreach (var command in requests.Keys)
        {
            root.Subcommands.Add(command);
        }

        return AgentCommandLine.Parse(root, requests, args, refused, parsed => new OverlayRequest(parsed.GetValue(trace)), out errors);
    }

    private static Command Settings(Option<bool> trace, Option<bool> pseudo, out Func<ParseResult, IThermiRequest> request)
    {
        var overlay = new Option<int>(OverlayOption)
        {
            Description = "The overlay to edit, counted from 1.",
            CustomParser = AgentCommandLine.Whole(1, int.MaxValue),
        };
        var item = new Option<string?>(ItemOption) { Description = "The id of the item to select." };
        var size = new Option<bool>(SizeOption) { Description = "Goes to the overlay's Size row." };
        request = it => new SettingsRequest(it.GetValue(overlay), it.GetValue(trace), it.GetValue(pseudo))
        {
            Item = it.GetValue(item),
            SizeRow = it.GetValue(size),
        };
        return new Command("settings", "Opens the settings window.") { overlay, item, size };
    }

    private static Command Preview(out Func<ParseResult, IThermiRequest> request)
    {
        var snapshot = new Argument<FileInfo>("snapshot")
        {
            Description = "A snapshot saved with thermi-sensors --dump, or a series saved with --seconds; an empty file paints the sensors stopped.",
        }.AcceptExistingOnly();
        var png = new Argument<string>("png") { Description = "Where the picture goes." };
        var scale = new Argument<float>("scale") { Arity = ArgumentArity.ZeroOrOne, CustomParser = Scale, DefaultValueFactory = _ => 1 };

        // A settings file that is not there paints the defaults, and is not created.
        var file = new Argument<string?>("settings") { Arity = ArgumentArity.ZeroOrOne, Description = "A settings file other than the user's." };
        request = it => new PreviewRequest(it.GetValue(snapshot)!.FullName, it.GetValue(png)!, it.GetValue(scale), it.GetValue(file));
        return new Command("preview", "Paints the overlay for a saved snapshot, without showing it.") { snapshot, png, scale, file };
    }

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
