using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Core.Bridge;

public enum HostOs
{
    Windows,
    Linux,
}

/// <summary>Where thermi's GNOME Shell extension, which shows the overlay in a GNOME session, is in it.</summary>
public enum ExtensionState
{
    Missing,
    Disabled,
    Enabled,
}

/// <summary>
/// What the window's host says of its desktop: its OS; its accent as <c>#rrggbb</c>, or null where the page keeps its own;
/// and in a GNOME session, where thermi's extension is, else null. Core decides from it which fixes the window offers.
/// </summary>
public sealed record HostDesktop(HostOs Os, string? Accent = null, ExtensionState? Extension = null)
{
    public static HostDesktop Windows { get; } = new(HostOs.Windows);

    /// <summary>
    /// The texts each OS but Windows says in its own words, which name Windows or its tools: each of Windows' keys, with the
    /// key of the words that stand for it there. The page reads every text through it, so it holds no OS of its own.
    /// </summary>
    public static IReadOnlyDictionary<HostOs, IReadOnlyDictionary<string, string>> OwnTexts { get; } =
        new Dictionary<HostOs, IReadOnlyDictionary<string, string>>
        {
            [HostOs.Linux] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Texts.Key(UiText.StartWithWindows)] = Texts.Key(UiText.StartAtSignIn),
                [Texts.Key(UiText.LanguageFromWindows)] = Texts.Key(UiText.LanguageFromSystem),
                [Texts.Key(CatalogText.CatalogEmptyHint)] = Texts.Key(CatalogText.CatalogEmptyHintOnLinux),
                [Texts.Key(SheetText.AnywhereInWindows)] = Texts.Key(SheetText.AnywhereOnTheDesktop),
                [Texts.Key(WindowText.HotkeysSet)] = Texts.Key(WindowText.HotkeysSetOnLinux),
                [Texts.Key(WindowText.HotkeysOff)] = Texts.Key(WindowText.HotkeysOffOnLinux),
            },
        };

    /// <summary>
    /// Whether a CPU's missing readings point to thermi's driver: Windows keeps them behind one, and Linux's kernel reads
    /// them with none, so there nothing says "driver".
    /// </summary>
    public bool OffersDriver => Os == HostOs.Windows;

    /// <summary>The extension's fix: installing it while it is missing, turning it on while it is off, or none.</summary>
    public ExtensionState? ExtensionFix =>
        Os == HostOs.Linux && Extension is ExtensionState.Missing or ExtensionState.Disabled ? Extension : null;

    /// <summary>Why a sensor reads nothing, as this desktop says it: where no driver is offered, a zero is nothing read.</summary>
    public NoValue? Why(NoValue? why) => why == NoValue.DriverMayBeMissing && !OffersDriver ? NoValue.ReadsNothing : why;

    public WindowStatus Of(WindowStatus status) =>
        status with { Driver = status.Driver && OffersDriver, Os = Os, Accent = Accent, Extension = ExtensionFix };
}
