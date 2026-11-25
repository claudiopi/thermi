using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Tray;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>thermi's menu and each overlay's as Windows' menu shows them, and an entry chosen by the text the user reads.</summary>
internal static class HostMenus
{
    /// <summary>thermi's menu's entries, or with an overlay, that overlay's, a separator as an empty text.</summary>
    public static List<(nuint Id, string Text, bool Checked)> Entries(OverlayHost host, int? overlay, List<MenuRow>? all = null)
    {
        var (rows, place) = host.Tray!.RowsOf(overlay);
        using var menu = ThermiMenus.Build(rows, place, all ?? []);
        return TestWindows.MenuEntries(menu);
    }

    public static void Choose(OverlayHost host, int? overlay, string text)
    {
        var all = new List<MenuRow>();
        var id = Entries(host, overlay, all).Single(entry => entry.Text == text).Id;
        host.Tray!.Choose(all[(int)id - 1], overlay);
    }

    /// <summary>The key that chooses <paramref name="entry"/> in the overlay's menu opened on the pill of <paramref name="item"/>.</summary>
    public static char KeyOf(OverlayHost host, int overlay, string item, MenuEntry entry)
    {
        var (rows, place) = host.Tray!.RowsOf(overlay, item: item);
        var all = new List<MenuRow>();
        using var menu = ThermiMenus.Build(rows, place, all);
        var id = (nuint)(all.FindIndex(row => row.Entry == entry) + 1);
        return TestWindows.AccessKey(menu, id) ?? throw new InvalidOperationException("The entry has no access key.");
    }
}
