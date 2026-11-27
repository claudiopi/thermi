using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// An overlay's menu as GTK's own popover menu, off GNOME, from the rows GNOME's extension takes: each group a section, set
/// apart as the Shell's are; a checked row checked, a grayed one insensitive, a submenu's rows on a page of their own
/// under the row they open from. A row's note shows where GTK shows a shortcut: as the hotkey it is, for a row that can be
/// chosen; beside a grayed row, which only says why, as a label of its own.
/// </summary>
internal sealed class PictureMenu
{
    private const string Group = "menu";

    // Core writes a hotkey as "Ctrl+Alt+T"; GTK reads its modifiers in brackets, and a few keys by their X names.
    private static readonly Dictionary<string, string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = "<Control>",
        ["Alt"] = "<Alt>",
        ["Shift"] = "<Shift>",
        ["Win"] = "<Super>",
    };

    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = "space",
        ["PageUp"] = "Page_Up",
        ["PageDown"] = "Page_Down",
    };

    private readonly Gio.Menu _root = Gio.Menu.New();
    private readonly Gio.SimpleActionGroup _actions = Gio.SimpleActionGroup.New();
    private readonly Dictionary<int, (Gio.Menu Menu, Gio.Menu? Section)> _menus = [];
    private readonly List<(string Id, Gtk.Widget Widget)> _custom = [];
    private readonly Gtk.PopoverMenu _popover;
    private readonly Action<int> _chosen;

    private PictureMenu(Action<int> chosen)
    {
        _popover = Gtk.PopoverMenu.NewFromModel(_root);
        _menus[-1] = (_root, null);
        _chosen = chosen;
    }

    /// <summary>
    /// The menu of <paramref name="rows"/>, not shown yet; the row chosen goes to <paramref name="chosen"/> once the menu
    /// closed, as the extension's choice comes after its menu closes.
    /// </summary>
    public static Gtk.PopoverMenu New(IReadOnlyList<ShellRow> rows, Action<int> chosen)
    {
        var menu = new PictureMenu(chosen);
        for (var index = 0; index < rows.Count; index++)
        {
            menu.Add(rows, index);
        }

        var popover = menu._popover;
        popover.InsertActionGroup(Group, menu._actions);
        menu._custom.ForEach(each => popover.AddChild(each.Widget, each.Id));
        popover.SetHasArrow(false);
        popover.SetHalign(Gtk.Align.Start);
        return popover;
    }

    private void Add(IReadOnlyList<ShellRow> rows, int index)
    {
        var row = rows[index];
        var (menu, section) = _menus[row.Parent];
        if (section is null || row.Starts)
        {
            section = Gio.Menu.New();
            menu.AppendSection(null, section);
            _menus[row.Parent] = (menu, section);
        }

        if (rows.Any(each => each.Parent == index))
        {
            var submenu = Gio.Menu.New();
            _menus[index] = (submenu, null);
            section.AppendSubmenu(row.Text, submenu);
            return;
        }

        var name = $"row{index}";
        var action = row.Checked ? Gio.SimpleAction.NewStateful(name, null, GLib.Variant.NewBoolean(true)) : Gio.SimpleAction.New(name, null);
        action.SetEnabled(!row.Grayed);
        action.OnActivate += (_, _) => Choose(index);
        _actions.AddAction(action);
        var item = Gio.MenuItem.New(row.Text, $"{Group}.{name}");
        if (row.Note is { } note && row.Grayed)
        {
            item.SetAttributeValue("custom", GLib.Variant.NewString(name));
            _custom.Add((name, Noted(row.Text, note)));
        }
        else if (row.Note is { } hotkey)
        {
            item.SetAttributeValue("accel", GLib.Variant.NewString(Accelerator(hotkey)));
        }

        section.AppendItem(item);
    }

    // What the row does may hide the overlay the menu hangs from, so it is done once the menu is gone.
    private void Choose(int index)
    {
        _popover.Popdown();
        GLib.Functions.IdleAdd(0, () =>
        {
            _chosen(index);
            return false;
        });
    }

    // A grayed row with its note at its end, read by a screen reader as the menu's entry it is.
    private static Gtk.Box Noted(string text, string note)
    {
        using var value = new GObject.Value(Gtk.AccessibleRole.MenuItem);
        using var role = new GObject.ConstructArgument("accessible-role", value);
        var box = Gtk.Box.NewWithProperties([role]);
        box.SetSpacing(24);
        var label = Gtk.Label.New(text);
        label.SetHexpand(true);
        label.SetXalign(0);
        box.Append(label);
        box.Append(Gtk.Label.New(note));
        box.SetSensitive(false);
        return box;
    }

    // A hotkey GTK cannot read shows nothing beside its row, as GTK shows any shortcut it cannot name.
    private static string Accelerator(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "";
        }

        var modifiers = string.Concat(parts[..^1].Select(part => Modifiers.GetValueOrDefault(part, "")));
        return modifiers + Keys.GetValueOrDefault(parts[^1], parts[^1]);
    }
}
