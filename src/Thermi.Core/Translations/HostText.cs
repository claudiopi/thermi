namespace Thermi.Core.Translations;

/// <summary>
/// The texts of the overlay's process beside its overlays: its menus' entries that depend on where they are,
/// the notices from thermi's icon, and the ghost of the overlay being edited.
/// </summary>
[Translated]
public enum HostText
{
    /// <summary>thermi's menu with one overlay: shows it again after its own Hide.</summary>
    ShowTheOverlay,

    /// <summary>An overlay's submenu in thermi's menu: shows it again.</summary>
    Show,

    /// <summary>An overlay's submenu in thermi's menu: hides it.</summary>
    Hide,

    /// <summary>thermi's menu with several overlays.</summary>
    ClickThroughAll,

    /// <summary>thermi's menu with several overlays.</summary>
    LockAll,

    /// <summary>The first-run notice's title.</summary>
    FirstRunTitle,

    /// <summary>The first-run notice: what to do first; a click on it opens the window.</summary>
    FirstRun,

    /// <summary>The first-run notice where the CPU reads zero: the driver it needs; a click on it opens the window, whose bar installs it.</summary>
    FirstRunDriver,

    /// <summary>The title of the notice after "Hide overlays" hid several.</summary>
    OverlaysHiddenTitle,

    /// <summary>The notice after "Hide overlays" hid several; a click on it shows them.</summary>
    OverlaysHidden,

    /// <summary>The same, naming the hotkey that hid them.</summary>
    OverlaysHiddenHotkey,

    /// <summary>The title of a notice about one overlay hidden, with its name.</summary>
    OverlayHiddenTitle,

    /// <summary>The notice after "Hide overlays" hid the only overlay; a click on it shows it.</summary>
    OverlayHiddenByFlag,

    /// <summary>The same, naming the hotkey that hid it.</summary>
    OverlayHiddenByFlagHotkey,

    /// <summary>The notice after an overlay's own Hide; a click on it shows it.</summary>
    OverlayHidden,

    /// <summary>"Notify me when critical": the title, with the item's name.</summary>
    CriticalTitle,

    /// <summary>The notice: the value, how long it has been critical, and that it comes once.</summary>
    Critical,

    /// <summary>The title of the notice that waited out a full-screen app, with the item's name.</summary>
    CriticalWhilePlayingTitle,

    /// <summary>That notice: the item's name, its peak and how long it was critical.</summary>
    CriticalWhilePlaying,

    /// <summary>A length in whole seconds.</summary>
    Seconds,

    /// <summary>The title of the notice thermi's tray icon shows as an overlay's click-through turns on, within 63 characters.</summary>
    ClickThroughOnTitle,

    /// <summary>The text of that notice, within 255 characters: where to turn it off, and that a click on it does.</summary>
    ClickThroughOnText,
}
