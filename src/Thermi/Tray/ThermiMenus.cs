using System.Runtime.InteropServices;
using Thermi.Core.Sensors;
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

    /// <summary>The last snapshot read, which names an item the file leaves nameless.</summary>
    public Snapshot? Known { get; init; }
}

/// <summary>
/// Core's menus as Windows' own popup menus, so they follow the theme, high contrast, the keyboard and Narrator: each row
/// gets an id in order, its submenu's rows included.
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
            choice = Track(menu, owner, place.Overlay is not null);
        }

        return choice > 0 && (int)choice <= all.Count ? all[(int)choice - 1] : null;
    }

    /// <summary>
    /// The popup menu of <paramref name="rows"/>, which the caller destroys, its submenus with it;
    /// <paramref name="all"/> gets each row at its id less one.
    /// </summary>
    internal static DestroyMenuSafeHandle Build(IReadOnlyList<MenuRow> rows, MenuPlace place, List<MenuRow> all)
    {
        var menu = PInvoke.CreatePopupMenu_SafeHandle();
        Fill(menu, rows, place, all, submenu: false);
        return menu;
    }

    // A heading names what the menu is about and does nothing, so it shows grayed.
    private static MENU_ITEM_FLAGS StateOf(MenuRow row) =>
        (row.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED)
        | (row.Heading ? MENU_ITEM_FLAGS.MF_GRAYED : MENU_ITEM_FLAGS.MF_ENABLED);

    private static void Fill(SafeHandle menu, IReadOnlyList<MenuRow> rows, MenuPlace place, List<MenuRow> all, bool submenu)
    {
        var texts = AccessKeys.Mark([.. rows.Select(row => TextOf(row, place, submenu))]);
        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];
            if (row.Starts && at > 0)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            }

            all.Add(row);
            var id = (nuint)all.Count;
            var text = texts[at] + (row.Hotkey is { } hotkey ? "\t" + hotkey : "");
            if (row.Entries.Count > 0)
            {
                Submenu(menu, row, place, all, text);
                continue;
            }

            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | StateOf(row), id, text);
            if (row.Graph is not null && row.Checked)
            {
                PInvoke.CheckMenuRadioItem(menu, (uint)id, (uint)id, (uint)id, (uint)MENU_ITEM_FLAGS.MF_BYCOMMAND);
            }

            if (row.Default)
            {
                PInvoke.SetMenuDefaultItem(menu, (uint)id, 0);
            }
        }
    }

    /// <summary>
    /// A row's text: the translation of its entry, or a variant that depends on where the menu is shown.
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
            return texts.Format(place.Numbers, entry, NameOf(config, row.Overlay ?? 0, texts));
        }

        if (entry is MenuEntry.EditItem or MenuEntry.Separate)
        {
            return texts.Format(place.Numbers, entry, Menus.NameOf(config, row, place.Known, texts, place.Numbers));
        }

        return row.Graph is { } graph ? texts.Of(graph)
            : entry == MenuEntry.Look && row.Look is { } look ? look
            : entry == MenuEntry.UndoGesture && place.UndoText is { } undo ? undo
            : texts.Of(entry);
    }

    /// <summary>An overlay's name, as the window's list says it.</summary>
    public static string NameOf(ThermiConfig config, int overlay, Texts texts) =>
        overlay >= config.Overlays.Count ? "" : OverlayNames.Of(config, overlay, texts);

    // The owner comes to the front first, or the menu would not close when the user clicks elsewhere, and gets a message
    // after, or the next menu would close as soon as it opens. While it shows, no overlay rises over it. An overlay's menu
    // keeps off the overlay, above or below it, so it covers none of its pills.
    private static unsafe nuint Track(SafeHandle menu, HWND owner, bool offOwner)
    {
        PInvoke.GetCursorPos(out var cursor);
        PInvoke.SetForegroundWindow(owner);
        AppMode.FollowInMenus();
        Topmost.MenuShowing = true;
        try
        {
            var away = new TPMPARAMS { cbSize = (uint)sizeof(TPMPARAMS) };
            var exclude = offOwner && PInvoke.GetWindowRect(owner, out away.rcExclude);
            var flags = TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | (exclude ? TRACK_POPUP_MENU_FLAGS.TPM_VERTICAL : TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN);
            var choice = (nuint)(int)PInvoke.TrackPopupMenuEx(menu, (uint)flags, cursor.X, cursor.Y, owner, exclude ? away : null);
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
        Fill(submenu, row.Entries, place with { Overlay = row.Overlay ?? place.Overlay }, all, submenu: true);
        if (PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)(nint)handle, text))
        {
            submenu.SetHandleAsInvalid();
        }
    }
}
