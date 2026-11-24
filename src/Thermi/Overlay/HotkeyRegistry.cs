using Thermi.Core;
using Thermi.Core.Settings;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Thermi.Overlay;

/// <summary>
/// The four hotkeys, registered with Windows on the host window, which gets their presses as <c>WM_HOTKEY</c> with the id
/// of <see cref="IdOf"/>. They are registered again for every settings the overlay holds, its own writes included, so the
/// status it answers is always about the hotkeys of the settings it holds; a combination another app holds, or that cannot
/// be read, is a failure.
/// </summary>
internal sealed class HotkeyRegistry(HWND hwnd) : IDisposable
{
    // Any id of the application's range other than the four's, for asking Windows whether a combination is free.
    private const int Probe = 0x0100;

    private HotkeysConfig? _tried;
    private HotkeyFailures _failed;
    private bool _resting;

    /// <summary>The window's "rest hotkeys" to the overlay's host, 1 to rest and 0 to take them again.</summary>
    public static uint RestMessage { get; } = PInvoke.RegisterWindowMessage("thermi-rest-hotkeys");

    /// <summary>The answer to "hotkey status": which failed, with the hash of the hotkeys tried.</summary>
    public long Status => Hotkeys.Status(_tried, _failed);

    /// <summary>The id a hotkey's presses come with; 0 is none, so ids count from 1.</summary>
    public static int IdOf(HotkeyRole role) => (int)role + 1;

    /// <summary>The hotkey a press's id is of, or null for an id of none.</summary>
    public static HotkeyRole? RoleOf(int id) => Hotkeys.Roles.Contains((HotkeyRole)(id - 1)) ? (HotkeyRole)(id - 1) : null;

    /// <summary>Asks the running overlay to let its hotkeys rest, or take them again, while a hotkey's row records; false when none answers.</summary>
    public static bool AskToRest(bool rest) => HiddenWindow.Ask(HiddenWindow.HostClass, RestMessage, rest ? 1u : 0u) is not null;

    /// <summary>
    /// The keys that type a character with AltGr, which Windows reads as Ctrl+Alt, on the calling thread's keyboard
    /// layout: a hotkey of Ctrl+Alt with one of them would take that character from every app.
    /// </summary>
    public static IReadOnlySet<int> AltGrKeys()
    {
        var layout = PInvoke.GetKeyboardLayout(0);
        var keys = new HashSet<int>();
        foreach (var character in Enumerable.Range('!', 'ÿ' - '!' + 1).Append('€'))
        {
            // The low byte is the key, the high its shift state, where Ctrl is 2 and Alt 4; -1 is no key at all.
            var scan = PInvoke.VkKeyScanEx((char)character, layout);
            if (scan != -1 && ((scan >> 8) & 6) == 6)
            {
                keys.Add(scan & 0xFF);
            }
        }

        return keys;
    }

    /// <summary>
    /// Whether another program holds <paramref name="combination"/>, as Windows says by refusing it to this thread too.
    /// One of thermi's own hotkeys is left to Core, which refuses another hotkey's combination; one that cannot be read
    /// is Core's to refuse too.
    /// </summary>
    public static bool HeldElsewhere(string combination, HotkeysConfig? own)
    {
        if (Hotkeys.Roles.Any(role => string.Equals(Hotkeys.Of(own, role), combination, StringComparison.OrdinalIgnoreCase)) || Hotkeys.Parse(combination) is not { } hotkey)
        {
            return false;
        }

        if (!PInvoke.RegisterHotKey(HWND.Null, Probe, (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)hotkey.Key))
        {
            return true;
        }

        PInvoke.UnregisterHotKey(HWND.Null, Probe);
        return false;
    }

    /// <summary>Registers the hotkeys of the settings the overlay holds; while they rest, only keeps them for when they wake.</summary>
    public void Register(HotkeysConfig? hotkeys)
    {
        Unregister();
        _tried = hotkeys;
        _failed = HotkeyFailures.None;
        if (_resting)
        {
            return;
        }

        foreach (var role in Hotkeys.Roles)
        {
            if (!Take(IdOf(role), Hotkeys.Of(hotkeys, role)))
            {
                _failed |= (HotkeyFailures)(1 << (int)role);
            }
        }
    }

    /// <summary>Lets every hotkey go while a hotkey's row records, so its keys reach the window, and takes them again after.</summary>
    public void Rest(bool rest)
    {
        if (rest == _resting)
        {
            return;
        }

        _resting = rest;
        Register(_tried);
    }

    public void Dispose() => Unregister();

    private void Unregister()
    {
        foreach (var role in Hotkeys.Roles)
        {
            PInvoke.UnregisterHotKey(hwnd, IdOf(role));
        }
    }

    // An off hotkey is no failure. Without no-repeat, holding the keys would hide and show the overlays in turn.
    private bool Take(int id, string? combination)
    {
        if (string.IsNullOrEmpty(combination))
        {
            return true;
        }

        return Hotkeys.Parse(combination) is { } hotkey && PInvoke.RegisterHotKey(hwnd, id, (HOT_KEY_MODIFIERS)hotkey.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)hotkey.Key);
    }
}
