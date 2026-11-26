using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;

namespace Thermi.Cli.Commands;

/// <summary>What <c>thermi</c> was asked to do; null from <see cref="AgentCommandLine.Parse"/>, with why, when its arguments make no sense.</summary>
internal interface IThermiRequest;

/// <summary><c>--help</c> or <c>--version</c>, which System.CommandLine answers itself.</summary>
internal sealed record BuiltInRequest(ParseResult Parsed) : IThermiRequest;

/// <summary>The agents' commands (<c>docs/adr/0013-agents-cli-and-mcp.md</c>), each answering one JSON document on stdout.</summary>
internal enum AgentVerb
{
    Status,
    Read,
    Watch,
    Sensors,
    Config,
    Edit,
    Monitors,
    Looks,
    Look,
    FindCpuFan,
    StartWithWindows,
    Mcp,
}

internal enum LookAction
{
    Wear,
    Save,
    Rename,
    Delete,
    Export,
    Import,
}

/// <summary>An agent's command with what it was given; an overlay is counted from 0, as the settings and every answer count it.</summary>
internal sealed record AgentRequest(AgentVerb Verb) : IThermiRequest
{
    public IReadOnlyList<string> Ids { get; init; } = [];

    public int Seconds { get; init; } = 1;

    public string? Search { get; init; }

    public IReadOnlyList<SensorKind> Types { get; init; } = [];

    /// <summary>An edit's JSON, a look's name or a look file's text.</summary>
    public string? Text { get; init; }

    /// <summary>A look's new name.</summary>
    public string? To { get; init; }

    public int? Overlay { get; init; }

    public LookAction Look { get; init; }

    /// <summary>For the finder, whether its fan becomes the CPU fan; for the start at sign-in, on or off, or null to ask.</summary>
    public bool? Switch { get; init; }

    /// <summary>Whether it may write the settings, a look, the finder's mark of its last run or the start at sign-in; the MCP server may write any.</summary>
    public bool Writes => Verb is AgentVerb.Edit or AgentVerb.FindCpuFan or AgentVerb.Mcp
        || (Verb == AgentVerb.Look && Look != LookAction.Export)
        || (Verb == AgentVerb.StartWithWindows && Switch is not null);
}

internal static class AgentCommandLine
{
    public const int MaxSeconds = 60;

    /// <summary>The answers' schema an agent asks for; today's is the only one.</summary>
    public static Option<int> Schema { get; } = new("--schema")
    {
        Recursive = true,
        Description = "The schema of the answers, 1 today.",
        DefaultValueFactory = _ => AgentSchema.Current,
        CustomParser = SchemaOf,
    };

    /// <summary>
    /// A host's command line, its own commands and the agents': the request <paramref name="args"/> make, help or the version,
    /// <paramref name="none"/>'s with no command, or null with <paramref name="errors"/> saying why they make no sense, as
    /// System.CommandLine or <paramref name="refused"/> finds.
    /// </summary>
    public static IThermiRequest? Parse(
        RootCommand root,
        Dictionary<Command, Func<ParseResult, IThermiRequest>> requests,
        IReadOnlyList<string> args,
        Func<ParseResult, IEnumerable<string>> refused,
        Func<ParseResult, IThermiRequest?> none,
        out IReadOnlyList<string> errors)
    {
        Add(root, requests);

        // A root with commands and no action of its own must be given one of them; a host's runs what none gives.
        root.SetAction(_ => 0);
        var parsed = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        errors = [.. parsed.Errors.Select(error => error.Message), .. refused(parsed)];
        return errors.Count > 0 ? null
            : parsed.Action is not null && parsed.Action != root.Action ? new BuiltInRequest(parsed)
            : requests.TryGetValue(parsed.CommandResult.Command, out var request) ? request(parsed) : none(parsed);
    }

    private static void Add(RootCommand root, Dictionary<Command, Func<ParseResult, IThermiRequest>> requests)
    {
        root.Options.Add(Schema);
        var looks = LookCommands(out var look);
        root.Subcommands.Add(look);
        foreach (var (command, request) in Reads().Concat(Changes()).Concat(looks))
        {
            requests[command] = request;
            if (command != look && !look.Subcommands.Contains(command))
            {
                root.Subcommands.Add(command);
            }
        }
    }

    private static Dictionary<Command, Func<ParseResult, IThermiRequest>> Reads()
    {
        var ids = new Argument<string[]>("ids")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description =
                $"Metrics as cpu.temperature, sensor ids, or items as {AgentReads.ItemPrefix}<id>; every metric and item but network addresses when none.",
        };
        var seconds = new Option<int>("--seconds")
        {
            Description = "Samples once a second for this long, 1 to 60.",
            DefaultValueFactory = _ => 1,
            CustomParser = Seconds,
        };
        var watched = new Argument<string[]>("ids") { Arity = ArgumentArity.ZeroOrMore, Description = ids.Description };
        var search = new Option<string?>("--search") { Description = "Finds sensors as the settings window's search does." };
        var types = new Option<SensorKind[]>("--type") { Description = "Only sensors of these types.", AllowMultipleArgumentsPerToken = true };
        return new()
        {
            [new Command("status", "Says whether the sensors read, an overlay runs, and what a person must fix.")] = _ => new AgentRequest(AgentVerb.Status),
            [new Command("read", "Reads metrics, sensors and items now, or their minimum, maximum and average.") { ids, seconds }] = it =>
                new AgentRequest(AgentVerb.Read) { Ids = it.GetValue(ids) ?? [], Seconds = it.GetValue(seconds) },
            [new Command("watch", "Reads as read does, one line a second, until stdin closes.") { watched }] = it =>
                new AgentRequest(AgentVerb.Watch) { Ids = it.GetValue(watched) ?? [] },
            [new Command("sensors", "Lists every sensor the service reads, by device.") { search, types }] = it =>
                new AgentRequest(AgentVerb.Sensors) { Search = it.GetValue(search), Types = it.GetValue(types) ?? [] },
            [new Command("config", "Gives the settings as thermi reads them.")] = _ => new AgentRequest(AgentVerb.Config),
            [new Command("monitors", "Lists the monitors, their bounds and taskbars, in Windows' pixels.")] = _ => new AgentRequest(AgentVerb.Monitors),
            [new Command("looks", "Lists the saved looks.")] = _ => new AgentRequest(AgentVerb.Looks),
        };
    }

    private static Dictionary<Command, Func<ParseResult, IThermiRequest>> Changes()
    {
        var edit = new Argument<string>("edit") { Description = "One edit, as JSON, with its kind." };
        var use = new Option<bool>("--use") { Description = "Makes the fan found the first overlay's CPU fan." };
        var on = new Argument<string?>("on-or-off") { Arity = ArgumentArity.ZeroOrOne };
        on.AcceptOnlyFromAmong("on", "off");
        return new()
        {
            [new Command("edit", "Makes one change of the settings, by the settings window's rules.") { edit }] = it =>
                new AgentRequest(AgentVerb.Edit) { Text = it.GetValue(edit) },
            [new Command("find-cpu-fan", "Loads the CPU for up to 30 s, at most once in 2 minutes, and finds the fan that answers.") { use }] = it =>
                new AgentRequest(AgentVerb.FindCpuFan) { Switch = it.GetValue(use) },
            [new Command("start-with-windows", "Says or sets whether the overlay starts at sign-in.") { on }] = it =>
                new AgentRequest(AgentVerb.StartWithWindows) { Switch = it.GetValue(on) is { } said ? said == "on" : null },
            [new Command("mcp", "Serves these commands as an MCP server on stdio.")] = _ => new AgentRequest(AgentVerb.Mcp),
        };
    }

    private static Dictionary<Command, Func<ParseResult, IThermiRequest>> LookCommands(out Command look)
    {
        var name = new Argument<string>("name") { Description = "The saved look's name." };
        var to = new Argument<string>("to") { Description = "Its new name." };
        var text = new Argument<string>("text") { Description = "A look file's text." };
        var exported = new Argument<string?>("name") { Arity = ArgumentArity.ZeroOrOne, Description = "A saved look, or the overlay's own when none." };
        var overlay = new Option<int?>("--overlay") { Description = "The overlay, counted from 0; every overlay when a look is worn without one." };
        var parts = new Dictionary<LookAction, Command>
        {
            [LookAction.Wear] = new("wear", "Wears a saved look.") { name, overlay },
            [LookAction.Save] = new("save", "Saves an overlay's look under a name.") { name, overlay },
            [LookAction.Rename] = new("rename", "Renames a saved look.") { name, to },
            [LookAction.Delete] = new("delete", "Deletes a saved look.") { name },
            [LookAction.Export] = new("export", "Gives a look's file as text.") { exported, overlay },
            [LookAction.Import] = new("import", "Wears the look of a look file's text.") { text, overlay },
        };
        look = new Command("look", "Wears, saves, renames, deletes, exports or imports a look.");
        foreach (var part in parts.Values)
        {
            look.Subcommands.Add(part);
        }

        return parts.ToDictionary(
            part => part.Value,
            part => (Func<ParseResult, IThermiRequest>)(it => new AgentRequest(AgentVerb.Look)
            {
                Look = part.Key,
                Text = part.Key switch
                {
                    LookAction.Import => it.GetValue(text),
                    LookAction.Export => it.GetValue(exported),
                    LookAction.Wear or LookAction.Save or LookAction.Rename or LookAction.Delete => it.GetValue(name),
                    _ => throw new InvalidOperationException($"Unknown look action {part.Key}."),
                },
                To = part.Key == LookAction.Rename ? it.GetValue(to) : null,
                Overlay = part.Key is LookAction.Rename or LookAction.Delete ? null : it.GetValue(overlay),
            }));
    }

    private static int SchemaOf(ArgumentResult result)
    {
        if (result.Tokens is [{ Value: var value }] && value == AgentSchema.Current.ToString(CultureInfo.InvariantCulture))
        {
            return AgentSchema.Current;
        }

        result.AddError(string.Create(CultureInfo.InvariantCulture, $"--schema takes {AgentSchema.Current}, the only schema thermi answers in."));
        return AgentSchema.Current;
    }

    private static int Seconds(ArgumentResult result)
    {
        if (result.Tokens is [var token]
            && int.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value is >= 1 and <= MaxSeconds)
        {
            return value;
        }

        result.AddError(string.Create(CultureInfo.InvariantCulture, $"--seconds takes a whole number from 1 to {MaxSeconds}."));
        return 1;
    }
}
