using System.Globalization;
using System.Text.Json;
using Thermi.Core;

namespace Thermi.Linux.Hotkeys;

/// <summary>
/// thermi's hotkeys as the Linux desktops name them: GTK's accelerators, as the GNOME Shell grabs them; the GlobalShortcuts
/// portal's triggers; X11's keysyms; and each hotkey's role as the settings file names it.
/// </summary>
internal static class KeyNames
{
    private const int F1 = 0x70;
    private const int F24 = 0x87;
    private const uint KeysymF1 = 0xFFBE;

    // The named keys of Core's hotkeys, by Windows' virtual-key code, with their X11 names and keysyms.
    private static readonly Dictionary<int, (string Name, uint Keysym)> Named = new()
    {
        [0x20] = ("space", 0x20),
        [0x21] = ("Page_Up", 0xFF55),
        [0x22] = ("Page_Down", 0xFF56),
        [0x23] = ("End", 0xFF57),
        [0x24] = ("Home", 0xFF50),
        [0x25] = ("Left", 0xFF51),
        [0x26] = ("Up", 0xFF52),
        [0x27] = ("Right", 0xFF53),
        [0x28] = ("Down", 0xFF54),
        [0x2D] = ("Insert", 0xFF63),
        [0x2E] = ("Delete", 0xFFFF),
    };

    /// <summary>The combination as GTK writes it, as "&lt;Control&gt;&lt;Alt&gt;F10".</summary>
    public static string Accelerator(Hotkey hotkey) =>
        string.Concat(Modifiers(hotkey.Modifiers, "<Control>", "<Alt>", "<Shift>", "<Super>")) + Name(hotkey.Key);

    /// <summary>The combination as the GlobalShortcuts portal takes a preferred trigger, as "CTRL+ALT+F10".</summary>
    public static string Trigger(Hotkey hotkey) => string.Join('+', [.. Modifiers(hotkey.Modifiers, "CTRL", "ALT", "SHIFT", "LOGO"), Name(hotkey.Key)]);

    /// <summary>The key's keysym, which X11 maps to the keyboard's keycodes.</summary>
    public static uint Keysym(int key) => key switch
    {
        >= 'A' and <= 'Z' => char.ToLowerInvariant((char)key),
        >= '0' and <= '9' => (uint)key,
        >= F1 and <= F24 => KeysymF1 + (uint)(key - F1),
        _ => Named[key].Keysym,
    };

    /// <summary>The hotkey's role as the settings file names its field, as "nextStyle".</summary>
    public static string RoleOf(HotkeyRole role) => JsonNamingPolicy.CamelCase.ConvertName(role.ToString());

    private static string Name(int key) => key switch
    {
        >= 'A' and <= 'Z' => char.ToLowerInvariant((char)key).ToString(),
        >= '0' and <= '9' => ((char)key).ToString(),
        >= F1 and <= F24 => string.Create(CultureInfo.InvariantCulture, $"F{key - F1 + 1}"),
        _ => Named[key].Name,
    };

    private static IEnumerable<string> Modifiers(KeyModifiers modifiers, string ctrl, string alt, string shift, string win) =>
        new[] { (KeyModifiers.Ctrl, ctrl), (KeyModifiers.Alt, alt), (KeyModifiers.Shift, shift), (KeyModifiers.Win, win) }
            .Where(each => modifiers.HasFlag(each.Item1))
            .Select(each => each.Item2);
}
