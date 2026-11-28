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

    /// <summary>A size of the overlay's Size menu, as its Size slider writes it.</summary>
    SizePercent,

    /// <summary>The title of the notice thermi's tray icon shows as an overlay's click-through turns on, within 63 characters.</summary>
    ClickThroughOnTitle,

    /// <summary>The text of that notice, within 255 characters: where to turn it off, and that a click on it does.</summary>
    ClickThroughOnText,

    /// <summary>
    /// The title of the notices that thermi cannot show its overlays: the GNOME Shell extension, which shows them on GNOME,
    /// cannot, or the desktop has nowhere to show them.
    /// </summary>
    GnomeExtensionTitle,

    /// <summary>That notice when the extension is not installed: GNOME has just asked to install it.</summary>
    GnomeExtensionMissing,

    /// <summary>That notice when the extension installed speaks another version of thermi's.</summary>
    GnomeExtensionOtherVersion,

    /// <summary>That notice when the extension is installed but off or broken.</summary>
    GnomeExtensionOff,

    /// <summary>That notice when the extension is installed but the running Shell has not read it, which it does at the next sign-in.</summary>
    GnomeExtensionNextSignIn,

    /// <summary>The missing extension's notice's button, which asks GNOME to install it again.</summary>
    GnomeExtensionInstall,

    /// <summary>The button of the notice of the extension of another version, which asks GNOME for its update.</summary>
    GnomeExtensionUpdate,

    /// <summary>
    /// The title of the notice that the GNOME Shell extension that runs is of an earlier version of thermi, whose update the
    /// Shell reads at the next sign-in: the overlays show through it meanwhile.
    /// </summary>
    GnomeExtensionEarlierTitle,

    /// <summary>That notice's text, which asks to sign out and back in, with no button.</summary>
    GnomeExtensionEarlier,

    /// <summary>That notice on a Wayland desktop with neither GNOME Shell nor layer surfaces, where nothing can show the overlays.</summary>
    NoOverlaySurfaces,
    /// <summary>A button of the notice of hidden overlays where a desktop's notices have buttons: the one overlay shows again.</summary>
    NoticeShowIt,

    /// <summary>That button when the notice is about several overlays: they all show again.</summary>
    NoticeShowThem,

    /// <summary>The button of the click-through notice that turns click-through off again.</summary>
    NoticeUndo,

    /// <summary>The button that closes a notice and does nothing else.</summary>
    NoticeGotIt,

    /// <summary>The first-run notice's button where a desktop's notices have buttons: it opens the window.</summary>
    NoticeOpenSettings,

    /// <summary>The title of the notice that the settings window ended with an error, as it opened or later.</summary>
    WindowFailedTitle,

    /// <summary>That notice's text: {0} is the window's log, which says why; its button opens the window again.</summary>
    WindowFailed,

    /// <summary>The text of the notice that no overlay can show, where gtk4-layer-shell, which shows them off GNOME, is not installed.</summary>
    NoOverlayLayerShell,
}
