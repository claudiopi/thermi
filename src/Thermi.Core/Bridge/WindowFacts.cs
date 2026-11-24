using System.Globalization;
using System.Text.Json.Serialization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Core.Bridge;

/// <summary>
/// What the window shows beside the settings, as C# decides it at each change: whether the overlay runs, which without it
/// leaves changes in the window and hotkeys idle; whether a CPU reading points to the missing driver; the hotkeys offered
/// and those the overlay could not register; and while the file cannot be used, where and why, worded.
/// </summary>
public sealed record WindowStatus(
    bool Overlay,
    bool Driver,
    IReadOnlyList<HotkeyRole> Hotkeys,
    IReadOnlyList<HotkeyRole> Failed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Problem);

/// <summary>The steps Undo and Redo would take, worded as "Critical 84 → 85 °C", or null when there is none.</summary>
public sealed record UndoSteps(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Undo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Redo);

/// <summary>A language thermi ships: its code, as the settings file writes it, and its name in itself, like "Español".</summary>
public sealed record Language(string Code, string Name);

/// <summary>What About and the language dropdown show: the version, every language, and the one Windows' own gives.</summary>
public sealed record About(string Version, IReadOnlyList<Language> Languages, string Windows);

/// <summary>What an undo or a redo changed back: the toast's words, and the item, or else the overlay, to show.</summary>
public sealed record Undone(
    string Said,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Item,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? Overlay);

/// <summary>What "Open in editor" and About's links open, outside the window.</summary>
public enum Opened
{
    SettingsFile,
    Source,
    Report,
    Notices,
}

/// <summary>The window's status, its undo steps, About and an undo's outcome, as the page reads them.</summary>
public static class WindowFacts
{
    /// <param name="config">The settings the window shows.</param>
    /// <param name="service">Whether the sensor service runs.</param>
    /// <param name="snapshot">What it read last, or null before its first reading.</param>
    /// <param name="lookSaved">Whether any look is saved, which the next-look hotkey needs.</param>
    /// <param name="hotkeyStatus">The overlay's answer about its hotkeys, or null when no overlay answered.</param>
    /// <param name="problem">Why the settings file cannot be used, or null.</param>
    /// <param name="format">The window's texts and number format.</param>
    public static WindowStatus Status(ThermiConfig config, ServiceState service, Snapshot? snapshot, bool lookSaved, long? hotkeyStatus, ConfigProblem? problem, ValueFormat format)
    {
        var failures = hotkeyStatus is { } status ? Core.Hotkeys.Trusted(status, config.Hotkeys) ?? HotkeyFailures.None : HotkeyFailures.None;
        var driver = service != ServiceState.Stopped && snapshot is not null && SensorMap.DriverMayBeMissing(snapshot);
        return new WindowStatus(
            hotkeyStatus is not null,
            driver,
            Core.Hotkeys.Offered(config, lookSaved),
            [.. Enum.GetValues<HotkeyRole>().Where(role => failures.HasFlag((HotkeyFailures)(1 << (int)role)))],
            problem?.Detail(format.Texts, format.Numbers) is { Length: > 0 } detail ? detail : null);
    }

    public static UndoSteps Steps(StepName? undo, StepName? redo, ValueFormat format) =>
        new(undo is null ? null : StepTexts.Of(undo, format), redo is null ? null : StepTexts.Of(redo, format));

    /// <summary>thermi's <paramref name="version"/>, every language by its own name, and the one Windows' display language gives.</summary>
    public static About About(string version, CultureInfo windows) =>
        new(version, [.. Texts.Languages.Select(code => new Language(code, Texts.Of(code).Of(UiText.LanguageName)))], Texts.For(null, windows).Of(UiText.LanguageName));

    /// <summary>
    /// What a step taken back as <paramref name="step"/> changed from <paramref name="before"/> to <paramref name="after"/>:
    /// the first item it changed that is still there, else the first overlay it changed.
    /// </summary>
    public static Undone UndoneOf(StepName step, ThermiConfig before, ThermiConfig after, ValueFormat format)
    {
        var was = before.Overlays.SelectMany(overlay => overlay.Items).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var item = after.Overlays.SelectMany(overlay => overlay.Items).FirstOrDefault(each => !was.TryGetValue(each.Id, out var old) || !Equals(old, each))?.Id;
        var overlay = Enumerable.Range(0, after.Overlays.Count).Where(at => at >= before.Overlays.Count || !Equals(before.Overlays[at], after.Overlays[at])).Cast<int?>().FirstOrDefault();
        return new Undone(StepTexts.Undone(step, format), item, item is null ? overlay : null);
    }
}
