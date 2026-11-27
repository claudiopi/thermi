using System.Drawing;
using System.Runtime.InteropServices;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tray;

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

    // A heading, or an entry refused now, does nothing, so it shows grayed.
    private static MENU_ITEM_FLAGS StateOf(MenuRow row) =>
        (row.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED)
        | (row.Heading ? MENU_ITEM_FLAGS.MF_GRAYED : MENU_ITEM_FLAGS.MF_ENABLED);

    private static void Fill(SafeHandle menu, IReadOnlyList<MenuRow> rows, MenuPlace place, List<MenuRow> all, bool submenu)
    {
        var texts = AccessKeys.Mark([.. rows.Select(row => MenuTexts.TextOf(row, place, submenu))], [.. rows.Select(row => row.Heading)]);
        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];
            if (row.Starts && at > 0)
            {
                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
            }

            all.Add(row);
            var id = (nuint)all.Count;
            var text = texts[at] + Beside(row, place.Texts);
            if (row.Entries.Count > 0)
            {
                Submenu(menu, row, place, all, text);
                continue;
            }

            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING | StateOf(row), id, text);
            if (row.Radio && row.Checked)
            {
                PInvoke.CheckMenuRadioItem(menu, (uint)id, (uint)id, (uint)id, (uint)MENU_ITEM_FLAGS.MF_BYCOMMAND);
            }

            if (row.Default)
            {
                PInvoke.SetMenuDefaultItem(menu, (uint)id, 0);
            }
        }
    }

    // Right-aligned after a tab, as Windows shows a shortcut.
    private static string Beside(MenuRow row, Texts texts) => MenuTexts.Beside(row, texts) is { } beside ? "\t" + beside : "";

    // The owner comes to the front first, or the menu would not close when the user clicks elsewhere, and gets a message
    // after, or the next menu would close as soon as it opens. While it shows, no overlay rises over it. An overlay's menu
    // opens where Core's MenuSpots puts it, beside the overlay; Windows, which measures the menu only as it shows it, moves
    // it to the other side, off the overlay it excludes, where it does not fit.
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
            var (at, flags) = exclude ? Beside(owner, away.rcExclude, cursor) : (cursor, TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN);
            flags |= TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD;
            var choice = (nuint)(int)PInvoke.TrackPopupMenuEx(menu, (uint)flags, at.X, at.Y, owner, exclude ? away : null);
            PInvoke.PostMessage(owner, PInvoke.WM_NULL, 0, 0);
            return choice;
        }
        finally
        {
            Topmost.MenuShowing = false;
        }
    }

    // The point the menu's top, below the overlay, or its bottom, above it, goes to, on the overlay's monitor.
    private static unsafe (Point At, TRACK_POPUP_MENU_FLAGS Flags) Beside(HWND owner, RECT overlay, Point cursor)
    {
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        var monitor = PInvoke.MonitorFromWindow(owner, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var screen = PInvoke.GetMonitorInfo(monitor, ref info) ? info.rcMonitor : overlay;
        var spot = MenuSpots.Beside(
            new PixelRect(overlay.left, overlay.top, overlay.right, overlay.bottom),
            new Position(cursor.X, cursor.Y),
            new PixelRect(screen.left, screen.top, screen.right, screen.bottom));
        var side = spot.Side == MenuSide.Above ? TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN : 0;
        return (new Point(spot.At.X, spot.At.Y), TRACK_POPUP_MENU_FLAGS.TPM_VERTICAL | side);
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
