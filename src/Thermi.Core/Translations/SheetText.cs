namespace Thermi.Core.Translations;

/// <summary>The keyboard shortcut sheet's own words: its sections and what the overlay does under the pointer.</summary>
[Translated]
public enum SheetText
{
    /// <summary>The section of the hotkeys, which work in any app.</summary>
    AnywhereInWindows,

    /// <summary>The section of the window's own keys.</summary>
    InThisWindow,

    /// <summary>The section of what the pointer does on the overlay.</summary>
    OnYourDesktop,

    SelectOnDesktop,

    /// <summary>The gesture that selects, where a key would show.</summary>
    Click,

    OpenOnDesktop,

    /// <summary>The gesture that opens the window.</summary>
    DoubleClick,

    PlacePill,

    /// <summary>The gesture that gives a pill its own place.</summary>
    DragOff,

    ResizeOverlay,

    /// <summary>Where a floating overlay is dragged to resize it.</summary>
    ItsCorner,
}
