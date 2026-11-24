using System.Text.Json;
using System.Text.Json.Nodes;
using Thermi.Core;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Ui.Web;

/// <summary>
/// The page's edits, each turned into the Core edit that decides it: the page names what to change, Core says whether it
/// may and how. A field Core has no edit for is refused as a call the window does not make, never written as given.
/// </summary>
internal static class PageEdits
{
    // A look's fields, and the overlay's layout, direction and labels, which its style holds.
    private static readonly HashSet<string> StyleFields = new(StringComparer.Ordinal)
    {
        "name", "preset", "label", "value", "opacity", "pills", "colors", "font", "corners", "gap", "effect", "units", "layout", "orientation", "noLabels",
    };

    /// <summary>Whether an edit is a chart's line, which the charts make, or a saved look worn, which the looks make.</summary>
    public static bool Elsewhere(PageEdit edit) => edit is LineEdit or TypedLineEdit or AddLineEdit or LookEdit;

    /// <summary>
    /// The Core edit for <paramref name="edit"/>, on the settings, the readings and the texts it is made with, and for a
    /// hotkey the keys the keyboard layout types with AltGr.
    /// </summary>
    public static Func<ThermiConfig, EditResult> Of(PageEdit edit, Snapshot snapshot, Texts texts, Func<IReadOnlySet<int>> altGrKeys) => edit switch
    {
        ThermiFieldEdit thermi => config => Thermi(thermi.Field, config, thermi.Value, altGrKeys),
        OverlayFieldEdit overlay => config => Overlay(overlay.Field, overlay.Overlay, config, overlay.Value),
        StyleFieldEdit style => config => Style(style.Field, style.Overlay, config, style.Value),
        ItemFieldEdit item => config => Item(new ItemField(item.Field, item.Item, item.Value, snapshot), config),
        AddItemEdit add => config => ItemEdits.AddSensor(config, add.Overlay, snapshot, add.Sensor, texts),
        RemoveItemEdit remove => config => ItemEdits.Delete(config, remove.Item),
        MoveItemEdit move => config => Move(config, move),
        NewOverlayEdit => OverlayEdits.Add,
        DuplicateOverlayEdit duplicate => config => OverlayEdits.Duplicate(config, duplicate.Overlay),
        MergeOverlayEdit merge => config => OverlayEdits.Merge(config, merge.Overlay, merge.Into),
        DeleteOverlayEdit delete => config => OverlayEdits.Delete(config, delete.Overlay),
        NudgeOverlayEdit nudge => config => OverlayEdits.Nudge(config, nudge.Overlay, nudge.Dx, nudge.Dy),
        ResetEdit => OverlayEdits.Reset,
        ResetLinesEdit reset => config => ItemEdits.ResetThreshold(config, reset.Item, snapshot),
        SensorNameEdit named => config => ThermiEdits.RenameSensor(config, snapshot, named.Sensor, named.Name, texts),
        _ => throw new BridgeCallException($"The window makes no {edit.GetType().Name}."),
    };

    private static EditResult Thermi(string field, ThermiConfig config, JsonElement value, Func<IReadOnlySet<int>> altGrKeys) => field switch
    {
        "language" => ThermiEdits.SetLanguage(config, Text(value)),
        "temperature" => ThermiEdits.SetTemperature(config, Read<TemperatureUnit>(value)),
        _ when HotkeyOf(field) is { } role => ThermiEdits.SetHotkey(config, role, Text(value), altGrKeys()),
        _ => throw NoField(field),
    };

    // A hotkey's field is "hotkeys." and its name in the settings file.
    private static HotkeyRole? HotkeyOf(string field) =>
        Hotkeys.Roles.Cast<HotkeyRole?>().FirstOrDefault(role => field == $"hotkeys.{JsonNamingPolicy.CamelCase.ConvertName(role.ToString()!)}");

    private static EditResult Overlay(string field, int overlay, ThermiConfig config, JsonElement value) => field switch
    {
        "name" => OverlayEdits.Rename(config, overlay, Text(value) ?? ""),
        "size" => OverlayEdits.SetSize(config, overlay, value.GetDouble()),
        "clickThrough" => OverlayEdits.SetClickThrough(config, overlay, Flag(value)),
        "locked" => OverlayEdits.SetLocked(config, overlay, Flag(value)),
        "oled" => OverlayEdits.SetOled(config, overlay, Flag(value)),
        "fade" => OverlayEdits.SetFade(config, overlay, Flag(value)),
        "hidden" => OverlayEdits.SetHidden(config, overlay, Flag(value)),
        "visibility" => OverlayEdits.SetVisibility(config, overlay, Read<Visibility>(value)),
        "graph" => OverlayEdits.SetGraph(config, overlay, Read<GraphStyle>(value)),
        "snap" => OverlayEdits.Snap(config, overlay, value.Deserialize<SnapConfig>(BridgeCalls.Options) ?? throw NoField(field)),
        _ => throw NoField(field),
    };

    // A preset sets the text alone, so the sizes it ignores leave the file; any other field of the look is set as given,
    // or with null back to its default, and Core's rules decide whether the look that makes may be written.
    private static EditResult Style(string field, int overlay, ThermiConfig config, JsonElement value) =>
        !StyleFields.Contains(field) ? throw NoField(field)
        : field == "preset" && value.ValueKind != JsonValueKind.Null ? StyleEdits.SetPreset(config, overlay, Read<StylePreset>(value))
        : StyleEdits.Restyle(config, overlay, style => Merged(style, field, value));

    private static EditResult Item(ItemField edit, ThermiConfig config)
    {
        var (field, id, value, snapshot) = edit;
        return field switch
        {
            "name" => ItemEdits.Rename(config, id, Text(value) ?? ""),
            "label" => ItemEdits.RenamePill(config, id, Text(value) ?? ""),
            "sensor" => Text(value) is { } sensor ? ItemEdits.ChangeSensor(config, id, snapshot, sensor) : ItemEdits.BackToAuto(config, id),
            "warnBy" => ItemEdits.SetWarnBy(
                config,
                id,
                snapshot,
                value.ValueKind == JsonValueKind.Null ? null : value.Deserialize<WarnByConfig>(BridgeCalls.Options)),
            "own" => ItemEdits.SetOwn(config, id, Flag(value)),
            "tray" => ItemEdits.SetTray(config, id, Flag(value)),
            "color" => ItemEdits.SetColor(config, id, Text(value)),
            "notify" => PowerEdits.SetNotify(config, id, Flag(value)),
            "offset" => PowerEdits.SetOffset(config, id, snapshot, value.ValueKind == JsonValueKind.Null ? 0 : value.GetDouble()),
            "sensors" => PowerEdits.SetSensors(
                config,
                id,
                snapshot,
                value.ValueKind == JsonValueKind.Null ? [] : value.Deserialize<IReadOnlyList<string>>(BridgeCalls.Options) ?? []),
            "combine" => PowerEdits.SetCombine(config, id, snapshot, Read<Combine>(value)),
            _ => Power(field, id, config, value),
        };
    }

    private static EditResult Power(string field, string id, ThermiConfig config, JsonElement value) => field switch
    {
        "stat" => PowerEdits.SetStat(config, id, Read<ItemStat>(value)),
        "smooth" => PowerEdits.SetSmooth(config, id, value.ValueKind == JsonValueKind.Null ? 0 : value.GetInt32()),
        "decimals" => PowerEdits.SetDecimals(config, id, value.ValueKind == JsonValueKind.Null ? null : value.GetInt32()),
        "bits" => PowerEdits.SetBits(config, id, Flag(value)),
        "graph" => PowerEdits.SetGraph(config, id, value.ValueKind == JsonValueKind.Null ? null : Read<GraphStyle>(value)),
        "alertOnly" => PowerEdits.SetAlertOnly(config, id, Flag(value)),
        _ => throw NoField(field),
    };

    private static StyleConfig Merged(StyleConfig style, string field, JsonElement value)
    {
        var fields = JsonSerializer.SerializeToNode(style, BridgeCalls.Options)?.AsObject() ?? [];
        fields.Remove(field);
        if (value.ValueKind != JsonValueKind.Null)
        {
            fields[field] = JsonNode.Parse(value.GetRawText());
        }

        return fields.Deserialize<StyleConfig>(BridgeCalls.Options) ?? style;
    }

    // An item moved to another overlay, or to one of its own, goes to its end there, then to its place.
    private static EditResult Move(ThermiConfig config, MoveItemEdit move)
    {
        var moved = OverlayEdits.MoveItem(config, move.Item, move.Overlay);
        return moved.Refused is null ? ItemEdits.Move(moved.Config, move.Item, move.Index) : moved;
    }

    private static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static bool Flag(JsonElement value) => value.ValueKind == JsonValueKind.True;

    // The page sends null for a choice's default, as for every other field.
    private static T Read<T>(JsonElement value)
        where T : struct, Enum => value.ValueKind == JsonValueKind.Null ? default : value.Deserialize<T>(BridgeCalls.Options);

    private static BridgeCallException NoField(string field) => new($"The window changes no field {field} this way.");

    private sealed record ItemField(string Field, string Id, JsonElement Value, Snapshot Snapshot);
}
