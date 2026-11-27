using System.Text;
using Thermi.Linux.Gnome;
using Thermi.Linux.Hotkeys;

namespace Thermi.Linux.Desktop;

/// <summary>
/// An overlay's menu as GTK's own popover menu, off GNOME, from the rows GNOME's extension takes: each group a section, set
/// apart as the Shell's are; a checked row checked, a radio's as a radio, a grayed one insensitive, the default one bold, a
/// submenu's rows on a page of their own under the row they open from. A row's access key, marked with "&amp;" as Windows'
/// menus mark it, is GTK's underlined mnemonic. A row's note shows where GTK shows a shortcut: as the hotkey it is, for a row
/// that can be chosen; beside a grayed row, which only says why, as a label of its own.
/// </summary>
internal sealed class PictureMenu
{
    private const string Group = "menu";

    // A radio's target, which GTK draws chosen where its action's state is the same.
    private const string On = "on";

    private readonly Gio.Menu _root = Gio.Menu.New();
    private readonly Gio.SimpleActionGroup _actions = Gio.SimpleActionGroup.New();
    private readonly Dictionary<int, (Gio.Menu Menu, Gio.Menu? Section)> _menus = [];
    private readonly List<(string Id, Gtk.Widget Widget)> _custom = [];
    private readonly Action<int> _chosen;
    private Action _popdown = () => { };

    private PictureMenu(Action<int> chosen)
    {
        _menus[-1] = (_root, null);
        _chosen = chosen;
    }

    /// <summary>The rows as GIO's model of a menu, which GTK draws, and its actions, in the group <c>menu</c>.</summary>
    public Gio.MenuModel Model => _root;

    public Gio.ActionGroup Actions => _actions;

    /// <summary>The model and the actions of a menu of <paramref name="rows"/>, with no widget of GTK's yet.</summary>
    public static PictureMenu Of(IReadOnlyList<ShellRow> rows, Action<int> chosen)
    {
        var menu = new PictureMenu(chosen);
        for (var index = 0; index < rows.Count; index++)
        {
            menu.Add(rows, index);
        }

        return menu;
    }

    /// <summary>
    /// The menu of <paramref name="rows"/>, not shown yet; the row chosen goes to <paramref name="chosen"/> once the menu
    /// closed, as the extension's choice comes after its menu closes.
    /// </summary>
    public static Gtk.PopoverMenu New(IReadOnlyList<ShellRow> rows, Action<int> chosen)
    {
        var menu = Of(rows, chosen);
        var popover = Gtk.PopoverMenu.NewFromModel(menu._root);
        menu._popdown = popover.Popdown;
        popover.InsertActionGroup(Group, menu._actions);
        menu._custom.ForEach(each => popover.AddChild(each.Widget, each.Id));
        popover.SetHasArrow(false);
        popover.SetHalign(Gtk.Align.Start);
        return popover;
    }

    /// <summary>
    /// A text with its access key marked as Windows' menus mark it, "&amp;" before the key and "&amp;&amp;" for one of its
    /// own, as GTK and a tray's menus underline it: "_" before the key, and "__" for one of its own.
    /// </summary>
    public static string Underlined(string marked) => Read(marked, key => $"_{key}", "__");

    // The text as it reads, with no access key, for a label that takes none.
    private static string Unmarked(string marked) => Read(marked, key => $"{key}", "_");

    private static string Read(string marked, Func<char, string> keyed, string underscore)
    {
        var text = new StringBuilder(marked.Length + 2);
        var at = 0;
        while (at < marked.Length)
        {
            if (marked[at] == '&' && at + 1 < marked.Length)
            {
                text.Append(marked[at + 1] == '&' ? "&" : keyed(marked[at + 1]));
                at += 2;
            }
            else
            {
                text.Append(marked[at] == '_' ? underscore : $"{marked[at]}");
                at++;
            }
        }

        return text.ToString();
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
            section.AppendSubmenu(Underlined(row.Text), submenu);
            return;
        }

        var name = $"row{index}";
        var action = row.Radio ? Gio.SimpleAction.NewStateful(name, GLib.VariantType.New("s"), GLib.Variant.NewString(row.Checked ? On : ""))
            : row.Checked ? Gio.SimpleAction.NewStateful(name, null, GLib.Variant.NewBoolean(true))
            : Gio.SimpleAction.New(name, null);
        action.SetEnabled(!row.Grayed);
        action.OnActivate += (_, _) => Choose(index);
        _actions.AddAction(action);
        var label = Underlined(row.Text);
        var item = Gio.MenuItem.New(row.Default ? $"<b>{GLib.Functions.MarkupEscapeText(label, -1)}</b>" : label, null);
        item.SetActionAndTargetValue($"{Group}.{name}", row.Radio ? GLib.Variant.NewString(On) : null);
        if (row.Default)
        {
            item.SetAttributeValue("use-markup", GLib.Variant.NewBoolean(true));
        }

        if (row.Note is { } note && row.Grayed)
        {
            item.SetAttributeValue("custom", GLib.Variant.NewString(name));
            _custom.Add((name, Noted(Unmarked(row.Text), note)));
        }
        else if (row.Note is { } hotkey && Core.Hotkeys.Parse(hotkey) is { } parsed && KeyNames.Knows(parsed.Key))
        {
            item.SetAttributeValue("accel", GLib.Variant.NewString(KeyNames.Accelerator(parsed)));
        }

        section.AppendItem(item);
    }

    // What the row does may hide the overlay the menu hangs from, so it is done once the menu is gone.
    private void Choose(int index)
    {
        _popdown();
        GLib.Functions.IdleAdd(0, () =>
        {
            _chosen(index);
            return false;
        });
    }

    // Read by a screen reader as the menu's entry it is.
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
}
