using System.Text;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>A menu as shown: Core's rows, submenus' rows after their own, the Shell's rows alike, and the overlay it is of, if any.</summary>
internal sealed record ShownMenu(IReadOnlyList<MenuRow> All, IReadOnlyList<ShellRow> Rows, int? Overlay);

/// <summary>
/// An overlay's menu as the desktop's own, the Shell's popup menu on GNOME and GTK's elsewhere: Core's rows, each with the
/// text every OS's menu gives it, flattened with the row each sits under, the default row bold and, in GTK's, each row's
/// access key, as Windows' menus mark them; shown beside the overlay where Core's <see cref="MenuSpots"/> says, and the
/// row chosen done by <see cref="MenuChoices"/>, as on every OS.
/// </summary>
internal sealed class OverlayMenu(OverlayParts parts, Action exit, IReadOnlyList<DesktopOverlay> overlays, Func<ShellScreens> screens) : IMenuHost
{
    /// <summary>The desktop's present time, for a choice that tells none, as a menu's, whose click came before it closed.</summary>
    public const uint Now = 0;

    // The system monitor each desktop ships, by the name XDG_CURRENT_DESKTOP gives the desktop; on another, as Cinnamon
    // or a session started by hand, the first of them found, the newer GNOME apps' last.
    private static readonly (string Desktop, string Program)[] TaskManagers =
    [
        ("GNOME", "gnome-system-monitor"),
        ("KDE", "plasma-systemmonitor"),
        ("XFCE", "xfce4-taskmanager"),
        ("MATE", "mate-system-monitor"),
        ("", "resources"),
    ];

    private ShownMenu _shown = new([], [], null);

    public Settings Settings => parts.Settings;

    public GestureUndo? Gestures => parts.Gestures;

    public ItemHistory History => parts.History;

    public int Overlays => overlays.Count;

    /// <summary>
    /// Shows overlay <paramref name="overlay"/>'s menu beside it, below or above as its screen has room, at the pointer's x,
    /// a point of thermi's pixels, on <paramref name="item"/> or its edge, so it covers none of its pills.
    /// </summary>
    public void Show(int overlay, Position cursor, string? item, ServiceState service, Snapshot? known)
    {
        if (overlay >= overlays.Count || overlays[overlay].Box is not { } box)
        {
            return;
        }

        _shown = Rows(overlay, item, fromTray: false, service, known);
        var stage = screens();
        var screen = stage.Screens.FirstOrDefault(each => each.Bounds.Contains(cursor))?.Bounds ?? box;
        var spot = MenuSpots.Beside(box, cursor, screen);
        var (left, top) = stage.Stage(new Position(spot.Anchor.Left, spot.Anchor.Top));
        var (right, bottom) = stage.Stage(new Position(spot.Anchor.Right, spot.Anchor.Bottom));
        _ = parts.Call(parts.Surfaces.Menu((uint)overlay, _shown.Rows, PillBox.FromEdges(left, top, right, bottom), spot.Side));
    }

    /// <summary>
    /// The rows of thermi's menu, with no <paramref name="overlay"/>, or of that overlay's, on <paramref name="item"/> or
    /// its edge, as the Shell's rows; an overlay that does not show has thermi's.
    /// </summary>
    public ShownMenu Rows(int? overlay, string? item, bool fromTray, ServiceState service, Snapshot? known)
    {
        var config = Settings.Config;
        var shown = overlay < Math.Min(config.Overlays.Count, overlays.Count) ? overlay : null;
        var undo = parts.Gestures.Offered(config);
        var looks = MenuChoices.Looks(Settings);
        var state = new MenuState(parts.Visibility.HideOverlays, Settings.Problem is not null, service, undo is null ? null : TimeSpan.Zero, looks, false)
        {
            Screens = screens().Screens,
            CanCopy = shown is { } on && overlays[on].Painted is not null,
        };
        var texts = Texts.For(config.Language, System.Globalization.CultureInfo.CurrentUICulture);
        var place = new MenuPlace(config, texts, parts.Culture, shown) { UndoText = undo is { } offered ? texts.Of(offered) : null, Known = known };
        var all = new List<MenuRow>();
        var rows = new List<ShellRow>();
        var menu = shown is { } at ? Menus.OfOverlay(config, at, state, item, fromTray) : Menus.Thermis(config, state);
        Flatten(menu, -1, place, (all, rows), keys: !fromTray && parts.MarksAccessKeys);
        return new ShownMenu(all, rows, shown);
    }

    /// <summary>
    /// The row at <paramref name="index"/> of the overlay's menu last shown was chosen; or, for -1, the menu of overlay
    /// <paramref name="overlay"/> closed, chosen or not, which frees its tooltips, whichever menu shows by then.
    /// </summary>
    public void Chosen(uint overlay, int index)
    {
        if (index >= 0)
        {
            Choose(_shown, index);
        }
        else if (overlay < overlays.Count)
        {
            overlays[(int)overlay].Pointer.MenuClosed();
        }
    }

    /// <summary>The row at <paramref name="index"/> of <paramref name="menu"/> was chosen.</summary>
    public void Choose(ShownMenu menu, int index)
    {
        if (index >= 0 && index < menu.All.Count)
        {
            MenuChoices.Choose(this, menu.All[index], menu.Overlay);
        }
    }

    public EditResult Settle(ThermiConfig before, ThermiConfig after) => parts.Settle(before, after);

    /// <summary>Opens the settings window on an overlay and an item of it, or brings the open one there.</summary>
    public void OpenSettings(int overlay, string? item) => OpenSettings(overlay, item, Now);

    /// <summary>Opens the settings window as the menu's choice does, on a click at a time of the desktop's.</summary>
    public void OpenSettings(int overlay, string? item, uint time)
    {
        parts.Channel.Click = time;
        parts.Window.OpenWindow(overlay, item);
    }

    public void OpenSize(int overlay)
    {
        parts.Channel.Click = Now;
        parts.Window.OpenSize(overlay);
    }

    // The sensors' service is the system's, which its package starts.
    public void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.TaskManager)
        {
            var desktops = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
            var own = TaskManagers.Where(each => desktops.Contains(each.Desktop, StringComparer.OrdinalIgnoreCase));
            parts.Channel.Click = Now;
            parts.Channel.Start(own.Concat(TaskManagers).Select(each => each.Program).FirstOrDefault(Installed) ?? TaskManagers[0].Program);
        }
        else if (entry == MenuEntry.CopyImage && overlay < overlays.Count && overlays[overlay].Png() is { } png)
        {
            _ = parts.Call(parts.Surfaces.Copy(png));
        }
    }

    public void UndoGesture(int overlay)
    {
        if (parts.Gestures.Take() is { } back)
        {
            Settings.Edit(back);
        }
    }

    public void Separate(int overlay, string item) => overlays[overlay].Separate(item, screens());

    public void HideOverlays(bool hidden) => parts.Visibility.HideOverlays = hidden;

    public void Exit() => exit();

    public void RequestPaint() => parts.RequestPaint();

    // The tray marks no access keys: its menu escapes every "_" of a text, as the protocol of the desktop's tray reads it.
    private static void Flatten(IReadOnlyList<MenuRow> rows, int parent, MenuPlace place, (List<MenuRow> All, List<ShellRow> Shown) to, bool keys)
    {
        var texts = rows.Select(row => MenuTexts.TextOf(row, place, parent >= 0)).ToList();
        var marked = keys ? [.. AccessKeys.Mark(texts, [.. rows.Select(row => row.Heading)]).Select(Underlined)] : texts;
        for (var at = 0; at < rows.Count; at++)
        {
            var row = rows[at];
            to.All.Add(row);
            to.Shown.Add(new ShellRow(parent, marked[at], MenuTexts.Beside(row, place.Texts), row.Checked, row.Heading, row.Starts)
            {
                Default = row.Default,
                Radio = row.Radio,
            });
            if (row.Entries.Count > 0)
            {
                Flatten(row.Entries, to.All.Count - 1, place with { Overlay = row.Overlay ?? place.Overlay }, to, keys);
            }
        }
    }

    // Windows' "&" before an access key as GTK's "_", whose own "_" is doubled, so it shows as one.
    private static string Underlined(string marked)
    {
        var text = new StringBuilder(marked.Length + 2);
        var at = 0;
        while (at < marked.Length)
        {
            if (marked[at] == '&' && at + 1 < marked.Length)
            {
                var key = marked[at + 1];
                text.Append(key == '&' ? "&" : $"_{key}");
                at += 2;
            }
            else
            {
                text.Append(marked[at] == '_' ? "__" : marked[at].ToString());
                at++;
            }
        }

        return text.ToString();
    }

    private static bool Installed(string program) => GLib.Functions.FindProgramInPath(program) is not null;
}
