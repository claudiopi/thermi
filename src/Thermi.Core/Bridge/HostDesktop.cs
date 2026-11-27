using Thermi.Core.Sensors;
using Thermi.Core.Settings;
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

/// <summary>The bar along a monitor's edge that an overlay snaps onto as onto Windows' taskbar, by what the desktop calls it.</summary>
public enum DesktopBar
{
    Taskbar,
    TopBar,
    Panel,

    /// <summary>No bar, as on Sway or Hyprland: the overlay sits on the screen's bottom edge.</summary>
    None,
}

/// <summary>
/// What the window's host says of its desktop: its OS; its accent as <c>#rrggbb</c>, or null where the page keeps its own;
/// in a GNOME session, where thermi's extension is, else null; whether it is in high contrast; and its bar. Core decides
/// from it which fixes the window offers and the words it says them in.
/// </summary>
public sealed record HostDesktop(HostOs Os, string? Accent = null, ExtensionState? Extension = null, bool Contrast = false, DesktopBar Bar = DesktopBar.Taskbar)
{
    public static HostDesktop Windows { get; } = new(HostOs.Windows);

    // Desktops with no bar of their own to snap onto, by the names XDG_CURRENT_DESKTOP gives them.
    private static readonly string[] Barless = ["sway", "Hyprland", "niri", "river", "wlroots"];

    /// <summary>
    /// The texts each OS but Windows says in its own words, which name Windows or its tools: each of Windows' keys, with the
    /// key of the words that stand for it there.
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

    /// <summary>The texts that name the taskbar, in the words of each other bar, as <see cref="OwnTexts"/> holds an OS's.</summary>
    public static IReadOnlyDictionary<DesktopBar, IReadOnlyDictionary<string, string>> BarTexts { get; } =
        new Dictionary<DesktopBar, IReadOnlyDictionary<string, string>>
        {
            [DesktopBar.TopBar] = BarWords(NameText.TopBar, PlaceText.OnTopBar, OverlayPageText.TopBarHint, OverlayPageText.MatchTopBar),
            [DesktopBar.Panel] = BarWords(NameText.Panel, PlaceText.OnPanel, OverlayPageText.PanelHint, OverlayPageText.MatchPanel),
            [DesktopBar.None] = BarWords(NameText.BottomEdge, PlaceText.AtBottomEdge, OverlayPageText.BottomEdgeHint, OverlayPageText.MatchDesktop),
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
        status with { Driver = status.Driver && OffersDriver, Os = Os, Accent = Accent, Extension = ExtensionFix, Contrast = Contrast, Bar = Bar };

    /// <summary>The bar of a Linux desktop, by the names its <c>XDG_CURRENT_DESKTOP</c> lists: GNOME's top bar, a barless one's none, else a panel.</summary>
    public static DesktopBar BarOf(string? desktops)
    {
        var names = (desktops ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Any(name => name.Equals("GNOME", StringComparison.OrdinalIgnoreCase)) ? DesktopBar.TopBar
            : names.Length == 0 || names.Any(name => Barless.Contains(name, StringComparer.OrdinalIgnoreCase)) ? DesktopBar.None
            : DesktopBar.Panel;
    }

    /// <summary>The texts as this desktop says them: its OS's own words and its bar's in place of Windows' and the taskbar's.</summary>
    public Texts Words(Texts texts) =>
        texts.With([.. OwnTexts.GetValueOrDefault(Os) ?? Empty, .. BarTexts.GetValueOrDefault(Bar) ?? Empty]);

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static Dictionary<string, string> BarWords(NameText name, PlaceText spot, OverlayPageText hint, OverlayPageText match) =>
        new(StringComparer.Ordinal)
        {
            [Texts.Key(NameText.Taskbar)] = Texts.Key(name),
            [Texts.Key(SnapSpot.Taskbar)] = Texts.Key(spot),
            [Texts.Key(OverlayPageText.TaskbarHint)] = Texts.Key(hint),
            [Texts.Key(PillTheme.System)] = Texts.Key(match),
        };
}
