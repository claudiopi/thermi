using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Tray;

/// <summary>
/// A tray icon's menu as <c>com.canonical.dbusmenu</c>, which KDE Plasma's tray and the other freedesktop trays draw
/// themselves: its rows are asked again each time the menu opens, as Windows builds its menu then, a row's id is its place
/// plus one, 0 the menu itself, and a separator stands before each row that starts a group. A click on a row chooses it.
/// </summary>
internal sealed class TrayMenu(Func<IReadOnlyList<ShellRow>> rowsNow, Action<int> chosen) : IBusObject
{
    /// <summary>Where a StatusNotifierItem's menu is, as its <c>Menu</c> property says.</summary>
    public const string Path = "/MenuBar";

    private static readonly BusInterface Shape = new(
        "com.canonical.dbusmenu",
        [
            ("GetLayout", "iias", "u(ia{sv}av)"), ("GetGroupProperties", "aias", "a(ia{sv})"), ("GetProperty", "is", "v"),
            ("Event", "isvu", ""), ("EventGroup", "a(isvu)", "ai"), ("AboutToShow", "i", "b"), ("AboutToShowGroup", "ai", "aiai"),
        ],
        [("ItemsPropertiesUpdated", "a(ia{sv})a(ias)"), ("LayoutUpdated", "ui"), ("ItemActivationRequested", "iu")],
        [("Version", "u"), ("TextDirection", "s"), ("Status", "s"), ("IconThemePath", "as")]);

    private IReadOnlyList<ShellRow> _rows = [];
    private List<Entry> _entries = [new(0, -1, [])];
    private uint _revision;

    public BusInterface Interface => Shape;

    public GLib.Variant Property(string name) => name switch
    {
        "Version" => GLib.Variant.NewUint32(3),
        "TextDirection" => GLib.Variant.NewString("ltr"),
        "Status" => GLib.Variant.NewString("normal"),
        _ => GLib.Variant.NewStrv([]),
    };

    public GLib.Variant? Call(string method, GLib.Variant args)
    {
        var first = args.GetChildValue(0);
        return method switch
        {
            "GetLayout" => Layout(first.GetInt32(), args.GetChildValue(1).GetInt32(), Strings(args.GetChildValue(2))),
            "GetGroupProperties" => Tuple(GroupOf(first, Strings(args.GetChildValue(1)))),
            "GetProperty" => Tuple(GLib.Variant.NewVariant(PropertyOf(first.GetInt32(), args.GetChildValue(1).GetString(out _)))),
            "Event" => Clicked(first.GetInt32(), args.GetChildValue(1).GetString(out _)) ? null : throw Unknown(first.GetInt32()),
            "EventGroup" => Tuple(Ints(Children(first).Where(each => !Clicked(IdOf(each), each.GetChildValue(1).GetString(out _))).Select(IdOf))),
            "AboutToShow" => Tuple(GLib.Variant.NewBoolean(first.GetInt32() == 0)),
            _ => AboutToShowGroup(Ints(first)),
        };
    }

    // The menu's rows as they are now, each under its parent, at the menu's top a parent of -1.
    private void Refresh()
    {
        _rows = rowsNow();
        _revision++;
        var entries = new List<Entry> { new(0, -1, []) };
        var separators = _rows.Count;
        for (var at = 0; at < _rows.Count; at++)
        {
            var row = _rows[at];
            if (row.Starts && entries.Any(entry => entry.Parent == row.Parent + 1))
            {
                entries.Add(new Entry(++separators, row.Parent + 1, new() { ["type"] = GLib.Variant.NewString("separator") }));
            }

            entries.Add(new Entry(at + 1, row.Parent + 1, PropertiesOf(row, opens: _rows.Any(child => child.Parent == at))));
        }

        _entries = entries;
    }

    private static Dictionary<string, GLib.Variant> PropertiesOf(ShellRow row, bool opens)
    {
        // A tray reads an underscore as the next letter's access key, and a tab as the start of what shows at the row's end.
        var label = PictureMenu.Underlined(row.Text) + (row.Note is { } note ? $"\t{note}" : "");
        var properties = new Dictionary<string, GLib.Variant> { ["label"] = GLib.Variant.NewString(label) };
        if (row.Grayed)
        {
            properties["enabled"] = GLib.Variant.NewBoolean(false);
        }

        if (row.Checked)
        {
            properties["toggle-type"] = GLib.Variant.NewString("checkmark");
            properties["toggle-state"] = GLib.Variant.NewInt32(1);
        }

        if (opens)
        {
            properties["children-display"] = GLib.Variant.NewString("submenu");
        }

        return properties;
    }

    private GLib.Variant Layout(int parent, int depth, IReadOnlyList<string> names)
    {
        if (parent == 0)
        {
            Refresh();
        }

        var entry = _entries.Find(each => each.Id == parent) ?? throw Unknown(parent);
        return GLib.Variant.NewTuple([GLib.Variant.NewUint32(_revision), LayoutOf(entry, depth, names)]);
    }

    // An entry with its children to depth levels down, all of them at -1.
    private GLib.Variant LayoutOf(Entry entry, int depth, IReadOnlyList<string> names) => GLib.Variant.NewTuple(
    [
        GLib.Variant.NewInt32(entry.Id),
        Dictionary(entry, names),
        GLib.Variant.NewArray(GLib.VariantType.New("v"), depth == 0 ? [] :
            [.. _entries.Where(child => child.Parent == entry.Id).Select(child => GLib.Variant.NewVariant(LayoutOf(child, depth - 1, names)))]),
    ]);

    // Each row of ids, every row for none, with the properties of names, every one for none.
    private GLib.Variant GroupOf(GLib.Variant ids, List<string> names)
    {
        var asked = Ints(ids).ToList();
        return GLib.Variant.NewArray(
            GLib.VariantType.New("(ia{sv})"),
            [.. _entries.Where(entry => entry.Id != 0 && (asked.Count == 0 || asked.Contains(entry.Id)))
                .Select(entry => GLib.Variant.NewTuple([GLib.Variant.NewInt32(entry.Id), Dictionary(entry, names)]))]);
    }

    private GLib.Variant PropertyOf(int id, string name) =>
        (_entries.Find(entry => entry.Id == id) ?? throw Unknown(id)).Properties.GetValueOrDefault(name)
        ?? throw new ArgumentException($"The menu's row {id} has no property {name}.", nameof(name));

    // Only a click chooses, and only a row with no rows under it, as Windows' menus do.
    private bool Clicked(int id, string happened)
    {
        if (_entries.All(entry => entry.Id != id))
        {
            return false;
        }

        if (happened == "clicked" && id > 0 && id <= _rows.Count && _rows.All(row => row.Parent != id - 1))
        {
            chosen(id - 1);
        }

        return true;
    }

    private GLib.Variant AboutToShowGroup(IEnumerable<int> ids)
    {
        var known = ids.ToList();
        return GLib.Variant.NewTuple([Ints(known.Where(id => id == 0)), Ints(known.Where(id => _entries.All(entry => entry.Id != id)))]);
    }

    private static GLib.Variant Dictionary(Entry entry, IReadOnlyList<string> names) => GLib.Variant.NewArray(
        GLib.VariantType.New("{sv}"),
        [.. entry.Properties.Where(pair => names.Count == 0 || names.Contains(pair.Key))
            .Select(pair => GLib.Variant.NewDictEntry(GLib.Variant.NewString(pair.Key), GLib.Variant.NewVariant(pair.Value)))]);

    private static IEnumerable<GLib.Variant> Children(GLib.Variant array) =>
        Enumerable.Range(0, (int)array.NChildren()).Select(at => array.GetChildValue((nuint)at));

    private static IEnumerable<int> Ints(GLib.Variant array) => Children(array).Select(each => each.GetInt32());

    private static GLib.Variant Ints(IEnumerable<int> ids) => GLib.Variant.NewArray(GLib.VariantType.New("i"), [.. ids.Select(GLib.Variant.NewInt32)]);

    private static List<string> Strings(GLib.Variant array) => [.. Children(array).Select(each => each.GetString(out _))];

    private static int IdOf(GLib.Variant each) => each.GetChildValue(0).GetInt32();

    private static GLib.Variant Tuple(GLib.Variant value) => GLib.Variant.NewTuple([value]);

    private static ArgumentException Unknown(int id) => new($"The menu has no row {id}.", nameof(id));

    private sealed record Entry(int Id, int Parent, Dictionary<string, GLib.Variant> Properties);
}
