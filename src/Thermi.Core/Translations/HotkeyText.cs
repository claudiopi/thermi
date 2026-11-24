namespace Thermi.Core.Translations;

/// <summary>What the window says in a hotkey's field: none set, one being typed, and one Windows or the overlay cannot take.</summary>
[Translated]
public enum HotkeyText
{
    /// <summary>A hotkey that is not set.</summary>
    HotkeyOff,

    /// <summary>A hotkey's field while it waits for the keys.</summary>
    HotkeyListening,

    /// <summary>A hotkey the overlay could not register, as written.</summary>
    HotkeyHeld,

    HotkeysNeedOverlay,
}
