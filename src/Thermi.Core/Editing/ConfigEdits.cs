using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// Why an edit could not be made. The window shows each as a text of its own from the translations, keyed by
/// its name; <see cref="Invalid"/> also carries the rule the settings would break, naming the field.
/// </summary>
[Translated]
public enum Refusal
{
    /// <summary>The item or overlay is no longer in the settings, as after a hand edit.</summary>
    NotFound,

    /// <summary>The sensor is not among those the service reads.</summary>
    SensorNotFound,

    SensorOfAnotherType,

    /// <summary>The item's own sensors read nothing now, so the type a new sensor must share is not known.</summary>
    TypeUnknown,

    /// <summary>Only an item that started from a metric can go back to Auto.</summary>
    NoAuto,

    WarningAboveCritical,

    /// <summary>A threshold is a number from 0 to 100, or to 10,000 RPM for a fan's speed.</summary>
    ThresholdOutOfRange,

    /// <summary>Thresholds color a meter, so an item without one has none.</summary>
    NoMeter,

    /// <summary>A meter is a temperature, a load, a duty or a fan speed: the values its lines can judge.</summary>
    CannotBeMeter,

    /// <summary>A combined item has no single sensor to meter itself with.</summary>
    CannotMeterItself,

    LabelEmpty,

    NameEmpty,

    /// <summary>A sensor's name holds at most <see cref="Sensors.SensorNames.MaxLength"/> characters.</summary>
    SensorNameTooLong,

    SizeOutOfRange,

    TooManyOverlays,

    LastOverlay,

    /// <summary>Alt+arrows move a floating overlay; a snapped one sits where its place puts it, so it moves by a drag or another place.</summary>
    Snapped,

    /// <summary>thermi has no translation for the language.</summary>
    UnknownLanguage,

    /// <summary>Alert-only and the graph both follow the item's meter.</summary>
    MeterNeeded,

    /// <summary>An offset lies within its type's range, as <see cref="PowerEdits.OffsetRange"/> gives it.</summary>
    OffsetOutOfRange,

    CombineNeedsTwo,

    /// <summary>A sum of temperatures means nothing; their highest or average does.</summary>
    SumOfTemperatures,

    /// <summary>The item's sensors read nothing now, so whether they are temperatures, which no sum takes, is not known.</summary>
    SumOfUnknown,

    StyleNameEmpty,

    StyleNameTooLong,

    /// <summary>Saved styles' names are unique whatever the case.</summary>
    StyleNameTaken,

    /// <summary>The saved style is no longer in its folder.</summary>
    StyleNotFound,

    /// <summary>A style file that is not one, or holds a value out of its range; <see cref="EditResult.Problem"/> names the field.</summary>
    BrokenStyle,

    /// <summary>A style file from a newer thermi, refused whole rather than read in part.</summary>
    NewerStyle,

    /// <summary>The combination cannot be a hotkey; <see cref="EditResult.Hotkey"/> says why.</summary>
    Hotkey,

    /// <summary>A style file Windows would not let thermi write or read; <see cref="EditResult.Problem"/> says why.</summary>
    StyleFileFailed,

    /// <summary>A file or text far larger than any style, which is never read whole.</summary>
    StyleTooLarge,

    /// <summary>Windows would not hand over what was pasted or dropped, as while another app holds the clipboard.</summary>
    PasteFailed,

    /// <summary>The settings would break a rule of the file; <see cref="EditResult.Problem"/> says which.</summary>
    Invalid,

    /// <summary>The settings file cannot be used, so nothing changes until it is fixed or restored.</summary>
    SettingsBroken,
}

/// <summary>
/// What an edit gives: valid settings, or the settings it was given and why it could not change them; a hotkey refused
/// also gives its own reason, as <see cref="Hotkeys.Judge"/> does.
/// </summary>
public sealed record EditResult(ThermiConfig Config, Refusal? Refused = null, string? Problem = null, HotkeyRefusal? Hotkey = null);

/// <summary>
/// The gate every edit goes through: the window writes only what passes it. Items are found by their id,
/// which is unique in the whole file, and overlays by their place in it.
/// </summary>
public static class ConfigEdits
{
    /// <summary>Any change, given ids where it added items, or refused when the settings would break a rule.</summary>
    public static EditResult Apply(ThermiConfig config, Func<ThermiConfig, ThermiConfig> change)
    {
        var changed = ItemIds.Assign(change(config));
        return ConfigRules.Problem(changed) is { } problem ? new EditResult(config, Refusal.Invalid, problem) : new EditResult(changed);
    }

    /// <summary>
    /// What changed from <paramref name="before"/> to <paramref name="after"/>, as a change of any settings: each part of it
    /// applies only where the settings still hold what it found, as undo does, so what another process wrote meanwhile
    /// stays. On <paramref name="before"/> itself it gives <paramref name="after"/>. Null when nothing changed.
    /// </summary>
    public static Func<ThermiConfig, ThermiConfig>? Replay(ThermiConfig before, ThermiConfig after)
    {
        var operations = ConfigDiff.Between(before, after);
        return operations.Count == 0
            ? null
            : config => operations.Aggregate(
                config,
                (current, operation) => operation.Redo(current) is { } changed && ConfigRules.Problem(changed) is null ? changed : current);
    }

    internal static EditResult Refuse(ThermiConfig config, Refusal refusal) => new(config, refusal);

    internal static EditResult OnOverlay(ThermiConfig config, int overlay, Func<EditResult> edit) =>
        HasOverlay(config, overlay) ? edit() : Refuse(config, Refusal.NotFound);

    internal static bool HasOverlay(ThermiConfig config, int overlay) => overlay >= 0 && overlay < config.Overlays.Count;

    internal static (int Overlay, int Index)? Find(ThermiConfig config, string id)
    {
        for (var overlay = 0; overlay < config.Overlays.Count; overlay++)
        {
            var index = IndexOf(config.Overlays[overlay].Items, id);
            if (index >= 0)
            {
                return (overlay, index);
            }
        }

        return null;
    }

    internal static int IndexOf(IReadOnlyList<ItemConfig> items, string id)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    internal static ItemConfig? Item(ThermiConfig config, string id) =>
        Find(config, id) is var (overlay, index) ? config.Overlays[overlay].Items[index] : null;

    internal static ThermiConfig WithOverlay(ThermiConfig config, int index, OverlayConfig overlay) =>
        config with { Overlays = [.. config.Overlays.Select((each, at) => at == index ? overlay : each)] };

    internal static ThermiConfig WithItems(ThermiConfig config, int overlay, IReadOnlyList<ItemConfig> items) =>
        WithOverlay(config, overlay, config.Overlays[overlay] with { Items = items });

    internal static ThermiConfig WithItem(ThermiConfig config, string id, Func<ItemConfig, ItemConfig> change) =>
        Find(config, id) is var (overlay, index)
            ? WithItems(config, overlay, [.. config.Overlays[overlay].Items.Select((item, at) => at == index ? change(item) : item)])
            : config;

    internal static EditResult Edit(ThermiConfig config, string id, Func<ItemConfig, ItemConfig> change) =>
        Find(config, id) is null ? Refuse(config, Refusal.NotFound) : Apply(config, current => WithItem(current, id, change));
}
