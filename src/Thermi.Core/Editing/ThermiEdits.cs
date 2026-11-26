using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>The edits of what belongs to thermi rather than to one overlay.</summary>
public static class ThermiEdits
{
    /// <summary>A language thermi ships, as its code, or null to follow Windows.</summary>
    public static EditResult SetLanguage(ThermiConfig config, string? language) =>
        language is null || Texts.Languages.Contains(language, StringComparer.Ordinal)
            ? ConfigEdits.Apply(config, current => current with { Language = language })
            : ConfigEdits.Refuse(config, Refusal.UnknownLanguage);

    /// <summary>
    /// Sets a hotkey as the file writes it, or turns it off with null; a combination <see cref="Hotkeys.Judge"/> refuses,
    /// with the keys the active layout types through AltGr, is refused with its reason. With all of them off, the file
    /// holds no hotkeys.
    /// </summary>
    public static EditResult SetHotkey(ThermiConfig config, HotkeyRole role, string? combination, IReadOnlySet<int> altGrKeys)
    {
        if (combination is not null && Hotkeys.Judge(combination, role, config.Hotkeys, altGrKeys) is { } refusal)
        {
            return new EditResult(config, Refusal.Hotkey, Hotkey: refusal);
        }

        var hotkeys = Hotkeys.With(config.Hotkeys, role, combination);
        return ConfigEdits.Apply(config, current => current with { Hotkeys = hotkeys == new HotkeysConfig() ? null : hotkeys });
    }

    /// <summary>
    /// "Click-through on all overlays", as thermi's menu and its hotkey have it:
    /// on for every overlay, or off for every one once all have it.
    /// </summary>
    public static EditResult ToggleClickThrough(ThermiConfig config)
    {
        var on = !config.Overlays.All(overlay => overlay.ClickThrough);
        return ConfigEdits.Apply(config, current => current with { Overlays = [.. current.Overlays.Select(overlay => overlay with { ClickThrough = on })] });
    }

    /// <summary>
    /// Gives a sensor the user's name, or with an empty one, or the library's own, gives it back the library's. The items
    /// thermi named after the sensor take the new name, in the language of <paramref name="texts"/>, English when there are none.
    /// </summary>
    public static EditResult RenameSensor(ThermiConfig config, Snapshot snapshot, string sensorId, string name, Texts? texts = null)
    {
        if (SensorMap.Find(snapshot, sensorId) is not { } sensor)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorNotFound);
        }

        var trimmed = name.Trim();
        if (trimmed.Length > SensorNames.MaxLength)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorNameTooLong);
        }

        var names = new Dictionary<string, string>(config.SensorNames ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        if (trimmed.Length == 0 || trimmed == sensor.Name)
        {
            names.Remove(sensorId);
        }
        else
        {
            names[sensorId] = trimmed;
        }

        var renamed = config with { SensorNames = names.Count == 0 ? null : names };
        return ConfigEdits.Apply(config, current => Names.Renamed(current, renamed, sensor, sensorId, texts ?? Texts.English));
    }

    /// <summary>Changes only how temperatures show: thresholds and offsets stay in °C.</summary>
    public static EditResult SetTemperature(ThermiConfig config, TemperatureUnit unit) =>
        ConfigEdits.Apply(config, current => current with { Temperature = unit });

    /// <summary>The person's choice of what AI agents may do; an agent's own edit of it is <see cref="Agents.AgentRules"/>'s.</summary>
    public static EditResult SetAgents(ThermiConfig config, AgentAccess access) =>
        ConfigEdits.Apply(config, current => current with { Agents = access });

    public static EditResult SetWindowTheme(ThermiConfig config, WindowTheme theme) =>
        ConfigEdits.Apply(config, current => current with { WindowTheme = theme });
}
