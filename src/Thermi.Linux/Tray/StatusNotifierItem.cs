using Thermi.App.Gestures;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Tray;

/// <summary>
/// One of thermi's icons as a StatusNotifierItem, as KDE Plasma's tray and the other freedesktop trays show it: its
/// picture, its tooltip and its id, the icon's key, so a tray keeps its place across starts; its menu is a
/// <see cref="TrayMenu"/> at <see cref="TrayMenu.Path"/>. A left click opens the menu, as on Windows: the item is a menu,
/// which the tray shows itself, and a tray that calls Activate or ContextMenu instead asks thermi for that menu alike.
/// </summary>
internal sealed class StatusNotifierItem(TrayPicture shows, Action<TrayPress> pressed) : IBusObject
{
    /// <summary>Where every tray looks for an item registered by its bus name.</summary>
    public const string Path = "/StatusNotifierItem";

    public const string NewIcon = "NewIcon";

    public const string NewToolTip = "NewToolTip";

    private const string Pixmaps = "a(iiay)";

    private static readonly BusInterface Shape = new(
        "org.kde.StatusNotifierItem",
        [("ContextMenu", "ii", ""), ("Activate", "ii", ""), ("SecondaryActivate", "ii", ""), ("Scroll", "is", "")],
        [("NewTitle", ""), ("NewIcon", ""), ("NewAttentionIcon", ""), ("NewOverlayIcon", ""), ("NewToolTip", ""), ("NewStatus", "s")],
        [
            ("Category", "s"), ("Id", "s"), ("Title", "s"), ("Status", "s"), ("WindowId", "i"), ("IconName", "s"), ("IconPixmap", Pixmaps),
            ("OverlayIconName", "s"), ("OverlayIconPixmap", Pixmaps), ("AttentionIconName", "s"), ("AttentionIconPixmap", Pixmaps),
            ("AttentionMovieName", "s"), ("ToolTip", $"(s{Pixmaps}ss)"), ("ItemIsMenu", "b"), ("Menu", "o"),
        ]);

    public BusInterface Interface => Shape;

    /// <summary>What the icon shows now; a tray hears it changed from the signals <see cref="NewIcon"/> and <see cref="NewToolTip"/>.</summary>
    public TrayPicture Shows { get; set; } = shows;

    public GLib.Variant Property(string name) => name switch
    {
        "Category" => GLib.Variant.NewString("Hardware"),
        "Id" => GLib.Variant.NewString(Shows.Key),
        "Title" => GLib.Variant.NewString("thermi"),
        "Status" => GLib.Variant.NewString("Active"),
        "WindowId" => GLib.Variant.NewInt32(0),
        "IconPixmap" => Pixmap(),
        "OverlayIconPixmap" or "AttentionIconPixmap" => GLib.Variant.NewArray(GLib.VariantType.New("(iiay)"), []),
        "ToolTip" => ToolTip(),
        "ItemIsMenu" => GLib.Variant.NewBoolean(true),
        "Menu" => GLib.Variant.NewObjectPath(TrayMenu.Path),
        _ => GLib.Variant.NewString(""),
    };

    public GLib.Variant? Call(string method, GLib.Variant args)
    {
        // A click, left or right, opens the menu, as ItemIsMenu asks; a middle click and the wheel do nothing on Windows'
        // tray either.
        if (method is "Activate" or "ContextMenu")
        {
            pressed(TrayPress.Menu);
        }

        return null;
    }

    private GLib.Variant Pixmap()
    {
        var picture = Shows.Picture;
        using var argb = GLib.Bytes.New(picture.Argb());
        return GLib.Variant.NewArray(GLib.VariantType.New("(iiay)"),
        [
            GLib.Variant.NewTuple(
            [
                GLib.Variant.NewInt32(picture.Width), GLib.Variant.NewInt32(picture.Height),
                GLib.Variant.NewFromBytes(GLib.VariantType.New("ay"), argb, true),
            ]),
        ]);
    }

    // The tooltip's first line is its title, as a tray sets it in bold, and the rest its text.
    private GLib.Variant ToolTip()
    {
        var lines = Shows.Tooltip.Split('\n', 2);
        return GLib.Variant.NewTuple(
        [
            GLib.Variant.NewString(""), GLib.Variant.NewArray(GLib.VariantType.New("(iiay)"), []),
            GLib.Variant.NewString(lines[0]), GLib.Variant.NewString(lines.Length > 1 ? lines[1] : ""),
        ]);
    }
}
