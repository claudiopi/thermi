using Thermi.Core;
using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// A hotkey never takes a combination that belongs to Windows, to the apps, to the window or to another hotkey.
/// </summary>
public sealed class HotkeyRefusalTests
{
    // The keys a Spanish layout types a character with through AltGr: |, @, #, ~ and €.
    private static readonly HashSet<int> SpanishAltGr = ['1', '2', '3', '4', 'E'];

    private static readonly HotkeysConfig Others = new() { NextStyle = "Ctrl+Alt+H" };

    // A combination for a hotkey, the hide one unless the row says, and why the edit refuses it, or null when it is set as
    // written; turning off the only hotkey left leaves the file with none.
    [Theory]
    [InlineData("Win+T", HotkeyRefusal.WithWin)]
    [InlineData("Alt+K", HotkeyRefusal.AltWithCharacter)]
    [InlineData("Ctrl+C", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+V", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+X", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+Z", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+Y", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+A", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+S", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+F", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+N", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+W", HotkeyRefusal.CommonShortcut)]
    [InlineData("Ctrl+Shift+T", HotkeyRefusal.CommonShortcut)]
    [InlineData("Alt+F4", HotkeyRefusal.SystemKeys)]
    [InlineData("Ctrl+Alt+Delete", HotkeyRefusal.SystemKeys)]
    [InlineData("Alt+Left", HotkeyRefusal.WindowShortcut)]
    [InlineData("Ctrl+Alt+E", HotkeyRefusal.AltGrCharacter)]
    [InlineData("alt+ctrl+h", HotkeyRefusal.OtherHotkey)]
    [InlineData("Shift+K", HotkeyRefusal.NeedsCtrlOrAlt)]
    [InlineData("F13", null)]
    [InlineData("Ctrl+Alt+T", null)]
    [InlineData("Ctrl+Shift+F9", null)]
    [InlineData("Ctrl+Ctrl+T", HotkeyRefusal.Unreadable)]
    [InlineData("Ctrl+Alt+H", null, HotkeyRole.NextStyle)]
    [InlineData(null, null, HotkeyRole.NextStyle)]
    public void HotkeysThatBelongToWindowsOrOtherAppsAreRefused(string? combination, HotkeyRefusal? refused, HotkeyRole role = HotkeyRole.Toggle)
    {
        var before = new ThermiConfig { Hotkeys = Others };
        var set = Hotkeys.With(Others, role, combination);

        var result = ThermiEdits.SetHotkey(before, role, combination, SpanishAltGr);

        Assert.Equal(refused, result.Hotkey);
        Assert.Equal(refused is null ? null : Refusal.Hotkey, result.Refused);
        Assert.Equal(refused is not null ? Others : set == new HotkeysConfig() ? null : set, result.Config.Hotkeys);
    }
}
