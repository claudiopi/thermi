namespace Thermi.Core.Translations;

/// <summary>The settings window's Settings page, its bars, the broken file's page, its toasts and its Undo and Redo.</summary>
[Translated]
public enum WindowText
{
    /// <summary>The Settings page's second heading, beside General.</summary>
    AboutSection,

    HotkeysField,

    /// <summary>Under Hotkeys: how many are set, and where they work.</summary>
    HotkeysSet,
    HotkeysOff,

    /// <summary>The four hotkeys' rows, each named by what it does.</summary>
    HotkeyToggle,
    HotkeyNextLook,
    HotkeyClickThrough,
    HotkeyResetMinMax,

    /// <summary>Under a hotkey's row while it records the next combination.</summary>
    HotkeyCapturing,

    /// <summary>Under "Click-through on all overlays": the way out without a hotkey.</summary>
    ClickThroughHotkeyHint,

    /// <summary>The name of a hotkey's × button, with the hotkey's name.</summary>
    ClearHotkey,

    FileAndReset,
    SettingsFile,
    OpenInEditor,
    ResetKeeps,

    /// <summary>The button that asks before a reset.</summary>
    ResetAsk,
    ResetTitle,
    ResetBody,
    Reset,

    AboutThermi,
    AboutVersion,
    UpdatesNotUpdating,
    UpdatesTurnedOff,
    UpdatesReady,
    UpdatesUnreached,
    UpdatesUpToDate,
    UpdatesNotChecked,
    UpdatesChecking,
    UpdatesCheckedNow,
    UpdatesBeta,
    RestartToUpdate,
    SourceCode,
    ReportProblem,
    ThirdPartyNotices,
    License,

    /// <summary>The bar while the sensor service is stopped: its title, the rest of its sentence, and its action.</summary>
    ServiceStopped,
    ServiceStoppedMore,
    StartIt,

    /// <summary>The rest of its sentence instead, while another program holds the pipe the service would serve.</summary>
    PipeTaken,

    /// <summary>The bar while the service's readings are stale.</summary>
    ReadingsStale,
    ReadingsStaleMore,

    /// <summary>The driver's bar, in each of its states, and its dialog.</summary>
    DriverNeeded,
    DriverAsks,
    InstallDriver,
    InstallForEveryone,
    WhatIsThis,
    DriverInstalling,
    DriverInstalled,
    DriverReading,
    DriverRestart,
    DriverRestartMore,
    DriverRefused,
    DriverRefusedMore,
    TryAgain,
    DriverTitle,
    DriverReads,
    DriverInstallLead,
    DriverInstallText,
    DriverAntiCheatLead,
    DriverAntiCheat,

    /// <summary>The broken file's page, under the line and its reason.</summary>
    BrokenKeeps,

    /// <summary>The toast after a removal, with the name of what went.</summary>
    Removed,

    /// <summary>A toast's Undo.</summary>
    UndoLink,

    /// <summary>The toast after a look file is worn, with the look's name.</summary>
    LookApplied,

    /// <summary>Undo's and Redo's tooltips, with the step they take.</summary>
    UndoStep,
    RedoStep,

    /// <summary>The shortcut sheet's line for Ctrl+V.</summary>
    ShortcutPaste,

    /// <summary>The look file menu's entry that gives every overlay this one's whole look.</summary>
    CopyLookToAll,

    /// <summary>The applied-look toast's action that saves it under a name.</summary>
    SaveLook,

    /// <summary>The message box that shows in place of the window where WebView2's runtime is missing: its title, and what to do, asked as Yes or No.</summary>
    WebViewMissingTitle,
    WebViewMissing,
    PageEndedTitle,
    PageEnded,
}
