using System.Text.Json;
using System.Text.Json.Nodes;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Agents;

/// <summary>An agent's edit that names nothing Core can decide: its text is not an edit, so the call is a bad argument.</summary>
public sealed class AgentInputException(string message) : Exception(message);

/// <summary>
/// An agent's edits: the window's own, as the bridge's JSON writes them (<see cref="PageEdit"/>), and two the window makes
/// by gestures, an item's lines set at once and an overlay left at a point of the desktop. Core's rules decide each.
/// </summary>
public static class AgentEdits
{
    public const string Threshold = "threshold";

    public const string Place = "place";

    /// <summary>
    /// The Core edit <paramref name="json"/> asks for, made on the readings of <paramref name="snapshot"/>, named in the
    /// language of <paramref name="texts"/>, and for a hotkey with the keys the keyboard layout types with AltGr.
    /// </summary>
    /// <exception cref="AgentInputException">When the text is no edit Core knows, or, once made, names a field or value none has.</exception>
    public static Func<ThermiConfig, EditResult> Of(string json, Snapshot snapshot, Texts texts, Func<IReadOnlySet<int>> altGrKeys)
    {
        var edit = Checked(() => Parsed(json, snapshot, texts, altGrKeys));
        // A field's name and value are read only as the edit is made, so a wrong one shows there.
        return config => Checked(() => edit(config));
    }

    private static T Checked<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or BridgeCallException or NotSupportedException)
        {
            throw new AgentInputException(ex.Message);
        }
    }

    private static Func<ThermiConfig, EditResult> Parsed(string json, Snapshot snapshot, Texts texts, Func<IReadOnlySet<int>> altGrKeys)
    {
        var edit = JsonNode.Parse(json) as JsonObject ?? throw new AgentInputException("An edit is a JSON object with a kind.");
        return edit["kind"]?.GetValue<string>() switch
        {
            Threshold => Lines(edit),
            Place => Placed(edit),
            null => throw new AgentInputException("An edit is a JSON object with a kind."),
            _ => Page(edit.Deserialize<PageEdit>(Options)!, snapshot, texts, altGrKeys),
        };
    }

    // An agent writes an edit's fields in any order, its kind among them; the page writes its kind first.
    private static readonly JsonSerializerOptions Options = new(BridgeCalls.Options) { AllowOutOfOrderMetadataProperties = true };

    private static Func<ThermiConfig, EditResult> Page(PageEdit edit, Snapshot snapshot, Texts texts, Func<IReadOnlySet<int>> altGrKeys) =>
        edit switch
        {
            LookEdit => throw new AgentInputException("A saved look is worn with the look command."),
            ThermiFieldEdit { Field: "agents" } access when Lets(access.Value) => config => ConfigEdits.Refuse(config, Refusal.AgentsOnlyByPerson),
            _ when PageEdits.Elsewhere(edit) => throw new AgentInputException($"A meter's lines are set at once with the {Threshold} edit."),
            _ => PageEdits.Of(edit, snapshot, texts, altGrKeys),
        };

    // Only a person lets agents change the settings: an agent may only stop them, whatever they may do now.
    private static bool Lets(JsonElement access) =>
        access.ValueKind == JsonValueKind.Null || (access.ValueKind == JsonValueKind.String && access.GetString() == "change");

    // A line given is set, null taking it out; a line left out stays as it is. Core refuses a warning past the critical.
    private static Func<ThermiConfig, EditResult> Lines(JsonObject edit)
    {
        var item = Required(edit, "item").GetValue<string>();
        var warning = edit["warning"]?.GetValue<double>();
        var critical = edit["critical"]?.GetValue<double>();
        return config =>
        {
            var now = ConfigEdits.Item(config, item)?.Threshold;
            return ItemEdits.SetThreshold(
                config,
                item,
                edit.ContainsKey("warning") ? warning : now?.Warning,
                edit.ContainsKey("critical") ? critical : now?.Critical);
        };
    }

    private static Func<ThermiConfig, EditResult> Placed(JsonObject edit)
    {
        var overlay = Required(edit, "overlay").GetValue<int>();
        var at = new Position(Required(edit, "x").GetValue<int>(), Required(edit, "y").GetValue<int>());
        return config => OverlayEdits.Drop(config, overlay, at);
    }

    private static JsonNode Required(JsonObject edit, string field) =>
        edit[field] ?? throw new AgentInputException($"A {edit["kind"]} edit takes {field}.");
}
