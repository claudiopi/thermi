using System.Runtime.InteropServices;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Tray;

/// <summary>Where a menu shows: thermi's own icon's, or an overlay's, from the overlay or its items' icons.</summary>
internal sealed record MenuPlace(ThermiConfig Config, Texts Texts, IFormatProvider Numbers, int? Overlay)
{
    /// <summary>The undo entry's text, with the last desktop gesture's name, when the overlay offers one.</summary>
    public string? UndoText { get; init; }
}

/// <summary>
/// Core's menus as Windows' own popup menus, so they follow the theme, high contrast, the keyboard and Narrator: each row
/// an id in order, its submenu's rows included, the default entry in bold and a hotkey beside its entry.
/// </summary>
internal static class ThermiMenus
{
    /// <summary>Shows <paramref name="rows"/> at the cursor, owned by <paramref name="owner"/>, and gives the row chosen, or null.</summary>
    public static MenuRow? Show(IReadOnlyList<MenuRow> rows, MenuPlace place, HWND owner)
    {
        var all = new List<MenuRow>();
        nuint choice;
        using (var menu = Build(rows, place, all))
        {
            choice = Track(menu, owner);
        }

        return choice > 0 && (int)choice <= all.Count ? all[(int)choice - 1] : null;
    }

    /// <summary>The popup menu of <paramref name="rows"/>, which the caller destroys, its submenus with it; <paramref name="all"/> gets each row at its id less one.</summary>
    internal static DestroyMenuSafeHandle Build(IReadOnlyList<MenuRow> rows, MenuPlace place, List<MenuRow> all)
    {
        var menu = PInvoke.CreatePopupMenu_SafeHandle();
        Fill(menu, rows, place, all, submenu: false);
        return menu;
    }

    private static void Fill(SafeHandle menu, IReadOnlyList<MenuRow> rows, MenuPlace place, List<MenuRow> all, bool submenu)
    {
        MenuRow? previous = null;
        foreach (var row in rows)
        {
            // "Settings…", the overlays' submenus and "Exit thermi" each start a group, as Windows' own menus group entries.
            if (!submenu && previous is not null && (row.Entry is MenuEntry.Settings or MenuEntry.Exit || (row.Entry == MenuEntry.Overlay && previous.Entry != MenuEntry.Overlay)))
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            }

            previous = row;

            all.Add(row);
            var id = (nuint)all.Count;
            var text = TextOf(row, place, submenu).Replace("&", "&&", StringComparison.Ordinal) + (row.Hotkey is { } hotkey ? "\t" + hotkey : "");
            if (row.Entries.Count > 0)
            {
                Submenu(menu, row, place, all, text);
                continue;
            }

            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | (row.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED), id, text);
            if (row.Default)
            {
                PInvoke.SetMenuDefaultItem(menu, (uint)id, 0);
            }
        }
    }

    /// <summary>
    /// A row's text: the translation of its entry, or what depends on where it is, as an overlay's name, "Hide" in an
    /// overlay's submenu, or "Lock all overlays" in thermi's menu with several.
    /// </summary>
    internal static string TextOf(MenuRow row, MenuPlace place, bool submenu)
    {
        var (config, texts, entry) = (place.Config, place.Texts, row.Entry);
        var several = place.Overlay is null && !submenu && config.Overlays.Count > 1;
        HostText? local = (entry, submenu, several) switch
        {
            (MenuEntry.ShowOverlay, true, _) => HostText.Show,
            (MenuEntry.ShowOverlay, false, _) when config.Overlays.Count == 1 => HostText.ShowTheOverlay,
            (MenuEntry.HideThisOverlay, true, _) => HostText.Hide,
            (MenuEntry.ClickThrough, _, true) => HostText.ClickThroughAll,
            (MenuEntry.Lock, _, true) => HostText.LockAll,
            _ => null,
        };
        if (local is { } text)
        {
            return texts.Of(text);
        }

        if (entry is MenuEntry.ShowOverlay or MenuEntry.Overlay)
        {
            return texts.Format(place.Numbers, entry, NameOf(config, row.Overlay ?? 0, texts, place.Numbers));
        }

        return entry == MenuEntry.Look && row.Look is { } look ? look
            : entry == MenuEntry.UndoGesture && place.UndoText is { } undo ? undo
            : texts.Of(entry);
    }

    /// <summary>An overlay's name, or without one, the window's picker's.</summary>
    public static string NameOf(ThermiConfig config, int overlay, Texts texts, IFormatProvider numbers) =>
        overlay >= config.Overlays.Count ? "" : config.Overlays[overlay].Name ?? OverlayNames.Of(config, overlay, texts, Desktop.Screens(), numbers);

    // The owner comes to the front first, or the menu would not close when the user clicks elsewhere, and gets a message
    // after, or the next menu would close as soon as it opens. While it shows, no overlay rises over it.
    private static nuint Track(SafeHandle menu, HWND owner)
    {
        PInvoke.GetCursorPos(out var cursor);
        PInvoke.SetForegroundWindow(owner);
        AppMode.FollowInMenus();
        Topmost.MenuShowing = true;
        try
        {
            var choice = (nuint)(int)PInvoke.TrackPopupMenu(menu, TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD, cursor.X, cursor.Y, owner, null);
            PInvoke.PostMessage(owner, PInvoke.WM_NULL, 0, 0);
            return choice;
        }
        finally
        {
            Topmost.MenuShowing = false;
        }
    }

    // The menu holds its submenu from here on and destroys it with itself.
    private static void Submenu(SafeHandle menu, MenuRow row, MenuPlace place, List<MenuRow> all, string text)
    {
        var handle = PInvoke.CreatePopupMenu();
        using var submenu = new DestroyMenuSafeHandle(handle);
        Fill(submenu, row.Entries, place with { Overlay = row.Overlay }, all, submenu: true);
        if (PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)(nint)handle, text))
        {
            submenu.SetHandleAsInvalid();
        }
    }
}
