using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Core;

/// <summary>Which hotkeys the overlay could not register, a bit each in the order of <see cref="HotkeyRole"/>.</summary>
[Flags]
public enum HotkeyFailures
{
    None = 0,
    Toggle = 1,
    NextStyle = 2,
    ClickThrough = 4,
    ResetMinMax = 8,
}

/// <summary>A hotkey's modifier keys, with the values Windows gives them, so the overlay passes them as they are.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A combination as Windows registers it: its modifiers and the virtual-key code of its key.</summary>
public readonly record struct Hotkey(KeyModifiers Modifiers, int Key);

/// <summary>
/// What a hotkey does: hide or show the overlays, the next look on all of them, click-through on all of them, or reset min
/// and max. On the bridge each is named as the settings file's <c>hotkeys</c> names its field.
/// </summary>
public enum HotkeyRole
{
    Toggle,

    [JsonStringEnumMemberName("nextStyle")]
    NextStyle,

    [JsonStringEnumMemberName("clickThrough")]
    ClickThrough,

    [JsonStringEnumMemberName("resetMinMax")]
    ResetMinMax,
}

/// <summary>Why a combination cannot be a hotkey: each takes a key Windows, the apps, the window or another hotkey already need.</summary>
[Translated]
public enum HotkeyRefusal
{
    /// <summary>A key alone, or with Shift alone, which every app types; F13 to F24 aside.</summary>
    NeedsCtrlOrAlt,

    /// <summary>Win's combinations are Windows' own.</summary>
    WithWin,

    /// <summary>Alt with a letter or a digit opens the menus of the app in front.</summary>
    AltWithCharacter,

    /// <summary>Ctrl or Ctrl+Shift with C, V, X, Z, Y, A, S, F, N, W or T.</summary>
    CommonShortcut,

    /// <summary>Alt+F4, Alt+Tab, Ctrl+Alt+Delete and Ctrl+Shift+Esc.</summary>
    SystemKeys,

    /// <summary>Alt with an arrow moves rows and overlays in the window.</summary>
    WindowShortcut,

    /// <summary>Ctrl+Alt is AltGr, so the key types a character on the active layout.</summary>
    AltGrCharacter,

    OtherHotkey,

    /// <summary>A combination written by hand that names no key thermi knows.</summary>
    Unreadable,
}

/// <summary>
/// The hotkeys as the file writes them, like "Ctrl+Alt+T", which of them a combination may be, and the "hotkey status"
/// both processes agree on: the overlay answers which it could not register, with a hash of the combinations it tried,
/// and the window trusts an answer only when that hash is of the combinations it shows, so an answer about older hotkeys
/// is never taken for the new ones.
/// </summary>
public static class Hotkeys
{
    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;
    private const long FailureBits = 0b1111;

    // F13 to F24, which no common keyboard has, so a hotkey of one alone takes no key another app needs.
    private const int F13 = 0x7C;
    private const int F24 = 0x87;
    private const int F4 = 0x73;
    private const int Tab = 0x09;
    private const int Escape = 0x1B;
    private const int Delete = 0x2E;

    private const KeyModifiers NotTyping = KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Win;

    private static readonly Dictionary<string, KeyModifiers> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = KeyModifiers.Ctrl,
        ["Alt"] = KeyModifiers.Alt,
        ["Shift"] = KeyModifiers.Shift,
        ["Win"] = KeyModifiers.Win,
    };

    // The order a combination is written in, as Windows names them.
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift", "Win"];

    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = Delete,
    };

    // Keys no hotkey takes, named only so their system combinations are refused with that reason.
    private static readonly Dictionary<string, int> JudgedKeys = new(NamedKeys, StringComparer.OrdinalIgnoreCase) { ["Tab"] = Tab, ["Esc"] = Escape };

    private static readonly Hotkey[] SystemCombinations =
    [
        new(KeyModifiers.Alt, F4), new(KeyModifiers.Alt, Tab), new(KeyModifiers.Alt | KeyModifiers.Shift, Tab),
        new(KeyModifiers.Ctrl | KeyModifiers.Alt, Delete), new(KeyModifiers.Ctrl | KeyModifiers.Shift, Escape),
    ];

    private static readonly int[] CommonKeys = ['C', 'V', 'X', 'Z', 'Y', 'A', 'S', 'F', 'N', 'W', 'T'];

    private static readonly int[] Arrows = [0x25, 0x26, 0x27, 0x28];

    /// <summary>The four hotkeys, in the order of the status's bits and of its hash.</summary>
    public static IReadOnlyList<HotkeyRole> Roles { get; } = [HotkeyRole.Toggle, HotkeyRole.NextStyle, HotkeyRole.ClickThrough, HotkeyRole.ResetMinMax];

    /// <summary>
    /// A combination as written: modifiers each once, then one key (a letter, a digit, F1 to F24 or a named key), joined
    /// by "+" in any case. A key needs Ctrl, Alt or Win, F13 to F24 aside, or it would take that key from every app, and
    /// with Shift alone the character it types.
    /// Null for off, the empty string included, and for anything else, which is reported as a hotkey that failed.
    /// </summary>
    public static Hotkey? Parse(string? combination) =>
        Split(combination, NamedKeys) is var (modifiers, key) && ((modifiers & NotTyping) != KeyModifiers.None || key is >= F13 and <= F24)
            ? new Hotkey(modifiers, key)
            : null;

    /// <summary>
    /// A combination pressed, as the file writes it: modifiers in the order Ctrl, Alt, Shift, Win, then the key, as
    /// "Ctrl+Alt+T". Null for what <see cref="Parse"/> refuses, such as a key without Ctrl, Alt or Win, or a key it cannot name.
    /// </summary>
    public static string? Write(KeyModifiers modifiers, int key)
    {
        if (NameOf(key) is not { } name)
        {
            return null;
        }

        var written = string.Join('+', ModifierOrder.Where(each => modifiers.HasFlag(Modifiers[each])).Append(name));
        return Parse(written) is null ? null : written;
    }

    /// <summary>The combination of one hotkey, or null when it is off.</summary>
    public static string? Of(HotkeysConfig? hotkeys, HotkeyRole role) => role switch
    {
        HotkeyRole.Toggle => hotkeys?.Toggle,
        HotkeyRole.NextStyle => hotkeys?.NextStyle,
        HotkeyRole.ClickThrough => hotkeys?.ClickThrough,
        HotkeyRole.ResetMinMax => hotkeys?.ResetMinMax,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown hotkey."),
    };

    /// <summary>The hotkeys with one of them set to <paramref name="combination"/>, or off with null.</summary>
    public static HotkeysConfig With(HotkeysConfig? hotkeys, HotkeyRole role, string? combination)
    {
        var current = hotkeys ?? new HotkeysConfig();
        return role switch
        {
            HotkeyRole.Toggle => current with { Toggle = combination },
            HotkeyRole.NextStyle => current with { NextStyle = combination },
            HotkeyRole.ClickThrough => current with { ClickThrough = combination },
            HotkeyRole.ResetMinMax => current with { ResetMinMax = combination },
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown hotkey."),
        };
    }

    /// <summary>
    /// The hotkeys the window offers, in their order: hiding and click-through always, the next look once a look is saved,
    /// and reset min and max once an item shows a minimum or maximum; one that is set stays offered, so it can be cleared.
    /// </summary>
    public static IReadOnlyList<HotkeyRole> Offered(ThermiConfig config, bool lookSaved) =>
        [.. Roles.Where(role => Of(config.Hotkeys, role) is not null || role switch
        {
            HotkeyRole.NextStyle => lookSaved,
            HotkeyRole.ResetMinMax => config.Overlays.Any(Menus.ShowsMinOrMax),
            HotkeyRole.Toggle or HotkeyRole.ClickThrough => true,
            _ => false,
        })];

    /// <summary>The 32-bit FNV-1a hash of the four combinations as written, in UTF-8, joined by line breaks, an off one being empty.</summary>
    public static uint Hash(HotkeysConfig? hotkeys)
    {
        var hash = FnvOffset;
        foreach (var letter in Encoding.UTF8.GetBytes(string.Join('\n', Roles.Select(role => Of(hotkeys, role) ?? ""))))
        {
            hash = (hash ^ letter) * FnvPrime;
        }

        return hash;
    }

    /// <summary>The overlay's answer: the hash of the hotkeys it tried in the upper 32 bits, and which failed in the low four.</summary>
    public static long Status(HotkeysConfig? tried, HotkeyFailures failed) => ((long)Hash(tried) << 32) | ((long)failed & FailureBits);

    /// <summary>Which of the hotkeys shown the overlay could not register, or null when its answer is about other hotkeys.</summary>
    public static HotkeyFailures? Trusted(long status, HotkeysConfig? shown) =>
        (uint)(status >>> 32) == Hash(shown) ? (HotkeyFailures)(status & FailureBits) : null;

    /// <summary>
    /// Why a combination cannot be the hotkey of <paramref name="role"/>, or null when it can: Windows', a common shortcut's
    /// or the window's own, one another hotkey of <paramref name="hotkeys"/> has, or one whose Ctrl+Alt types a character
    /// with AltGr on the active layout, whose keys that do are <paramref name="altGrKeys"/>.
    /// </summary>
    public static HotkeyRefusal? Judge(string combination, HotkeyRole role, HotkeysConfig? hotkeys, IReadOnlySet<int> altGrKeys)
    {
        if (Split(combination, JudgedKeys) is not var (modifiers, key))
        {
            return HotkeyRefusal.Unreadable;
        }

        var hotkey = new Hotkey(modifiers, key);
        return Refused(hotkey, altGrKeys)
            ?? (Roles.Where(other => other != role).Any(other => Parse(Of(hotkeys, other)) == hotkey) ? HotkeyRefusal.OtherHotkey : null);
    }

    // The first rule a combination breaks, in this order, gives the reason.
    private static HotkeyRefusal? Refused(Hotkey hotkey, IReadOnlySet<int> altGrKeys) =>
        Rules.FirstOrDefault(rule => rule.Breaks(hotkey, hotkey.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt), altGrKeys)).Reason;

    private static readonly (Func<Hotkey, KeyModifiers, IReadOnlySet<int>, bool> Breaks, HotkeyRefusal? Reason)[] Rules =
    [
        ((hotkey, _, _) => SystemCombinations.Contains(hotkey), HotkeyRefusal.SystemKeys),
        ((hotkey, _, _) => hotkey.Key is Tab or Escape, HotkeyRefusal.Unreadable),
        ((hotkey, _, _) => hotkey.Modifiers.HasFlag(KeyModifiers.Win), HotkeyRefusal.WithWin),
        ((hotkey, ctrlAlt, _) => ctrlAlt == KeyModifiers.None && hotkey.Key is < F13 or > F24, HotkeyRefusal.NeedsCtrlOrAlt),
        ((hotkey, ctrlAlt, _) => ctrlAlt == KeyModifiers.Alt && hotkey.Key is (>= 'A' and <= 'Z') or (>= '0' and <= '9'), HotkeyRefusal.AltWithCharacter),
        ((hotkey, ctrlAlt, _) => ctrlAlt == KeyModifiers.Ctrl && CommonKeys.Contains(hotkey.Key), HotkeyRefusal.CommonShortcut),
        ((hotkey, ctrlAlt, _) => ctrlAlt == KeyModifiers.Alt && Arrows.Contains(hotkey.Key), HotkeyRefusal.WindowShortcut),
        ((hotkey, _, altGr) => hotkey.Modifiers == (KeyModifiers.Ctrl | KeyModifiers.Alt) && altGr.Contains(hotkey.Key), HotkeyRefusal.AltGrCharacter),
    ];

    // The modifiers and the key of a combination, whatever they are; null for one that is off or cannot be read.
    private static (KeyModifiers Modifiers, int Key)? Split(string? combination, IReadOnlyDictionary<string, int> named)
    {
        if (string.IsNullOrWhiteSpace(combination))
        {
            return null;
        }

        var parts = combination.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            if (!Modifiers.TryGetValue(part, out var modifier) || modifiers.HasFlag(modifier))
            {
                return null;
            }

            modifiers |= modifier;
        }

        return KeyOf(parts[^1], named) is { } key ? (modifiers, key) : null;
    }

    private static string? NameOf(int key) => key switch
    {
        (>= 'A' and <= 'Z') or (>= '0' and <= '9') => ((char)key).ToString(CultureInfo.InvariantCulture),
        >= 0x70 and <= F24 => string.Create(CultureInfo.InvariantCulture, $"F{key - 0x6F}"),
        _ => NamedKeys.FirstOrDefault(pair => pair.Value == key).Key,
    };

    private static int? KeyOf(string key, IReadOnlyDictionary<string, int> named)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            return char.ToUpperInvariant(key[0]);
        }

        if (key.Length is 2 or 3
            && key[0] is 'F' or 'f'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 24)
        {
            return 0x6F + number;
        }

        return named.TryGetValue(key, out var found) ? found : null;
    }
}
