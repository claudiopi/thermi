using System.Globalization;
using Thermi.Core;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests;

/// <summary>Hotkeys as written in the file, and the status the overlay answers about them.</summary>
public sealed class HotkeyTests
{
    // Registered alone, or with Shift only, a key would take itself or the character it types from every app; F13 to F24
    // are on no common keyboard, so they may.
    [Theory] // Rows: each an operand of the parse, or an end of F1 to F24 and of F13 to F24
    [InlineData("Ctrl+")]
    [InlineData("Hyper+T")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+TT")]
    [InlineData("Shift+A")]
    [InlineData("F12")]
    public void AnythingElseIsNoCombination(string? written) => Assert.Null(Hotkeys.Parse(written));

    // The window writes what was pressed as the file does, and only what the overlay can register; the file is read
    // as Windows registers it, however a person typed it.
    [Theory] // Rows: each a static modifier-order or key-name entry, or an end of F13 to F24
    [InlineData(KeyModifiers.Alt | KeyModifiers.Ctrl, 'T', "Ctrl+Alt+T", "Ctrl+Alt+T")]
    [InlineData(KeyModifiers.Win | KeyModifiers.Shift, '7', "Shift+Win+7", "shift + win + 7")]
    [InlineData(KeyModifiers.None, 0x7C, "F13", "F13")]
    [InlineData(KeyModifiers.None, 0x87, "F24", "f24")]
    [InlineData(KeyModifiers.Alt, 0x22, "Alt+PageDown", "Alt+PageDown")]
    [InlineData(KeyModifiers.Shift, 'A', null, null)]
    [InlineData(KeyModifiers.Ctrl, 0xBA, null, null)]
    public void APressedCombinationIsWrittenAsTheFileHoldsIt(KeyModifiers modifiers, int key, string? written, string? typed)
    {
        Hotkey? expected = written is null ? null : new Hotkey(modifiers, key);

        Assert.Equal(written, Hotkeys.Write(modifiers, key));
        Assert.Equal(expected, Hotkeys.Parse(written));
        Assert.Equal(expected, Hotkeys.Parse(typed));
    }

    // The status crosses processes as a number: the FNV-1a hash of the four combinations as written, an off one empty and a
    // line break between them, so the case counts and none can pass for another.
    [Fact]
    public void AStatusIsTrustedOnlyForTheHotkeysItWasAbout()
    {
        var tried = new HotkeysConfig { Toggle = "Ctrl+Alt+T", NextStyle = "Ctrl+Alt+S" };

        var status = Hotkeys.Status(tried, HotkeyFailures.NextStyle);

        Assert.Equal(0xE5A529EDL << 32, Hotkeys.Status(null, HotkeyFailures.None));
        Assert.Equal(HotkeyFailures.NextStyle, Hotkeys.Trusted(status, tried));
        Assert.Equal(HotkeyFailures.None, Hotkeys.Trusted(Hotkeys.Status(null, HotkeyFailures.None), new HotkeysConfig()));
        Assert.Null(Hotkeys.Trusted(status, tried with { NextStyle = "Ctrl+Alt+N" }));
        Assert.Null(Hotkeys.Trusted(status, tried with { Toggle = "ctrl+alt+t" }));
        Assert.Null(Hotkeys.Trusted(
            Hotkeys.Status(new HotkeysConfig { Toggle = "Ctrl+Alt+T" }, HotkeyFailures.None),
            new HotkeysConfig { NextStyle = "Ctrl+Alt+T" }));
        Assert.Equal(
            HotkeyFailures.Toggle | HotkeyFailures.NextStyle | HotkeyFailures.ClickThrough | HotkeyFailures.ResetMinMax,
            Hotkeys.Trusted(Hotkeys.Status(tried, (HotkeyFailures)0xFF), tried));

        // The window says a hotkey failed only on a status it trusts, and that the overlay runs only on one it got.
        Assert.Equal([HotkeyRole.NextStyle], Shown(tried, status).Failed);
        Assert.Empty(Shown(tried with { NextStyle = "Ctrl+Alt+N" }, status).Failed);
        Assert.Empty(Shown(tried, null).Failed);
        Assert.False(Shown(tried, null).Overlay);
    }

    private static WindowStatus Shown(HotkeysConfig hotkeys, long? answer) =>
        WindowFacts.Status(
            new ThermiConfig { Hotkeys = hotkeys },
            ServiceState.Running,
            null,
            lookSaved: true,
            answer,
            null,
            new ValueFormat(Texts.English, CultureInfo.InvariantCulture));
}
