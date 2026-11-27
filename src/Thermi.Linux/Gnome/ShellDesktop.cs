using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;
using Thermi.Linux.Painting;
using Thermi.Linux.Tray;

namespace Thermi.Linux.Gnome;

/// <summary>
/// thermi's side of its extension's icons in the top bar and its hotkeys (src/Thermi.Gnome/desktop.js), on the same object
/// as <see cref="ShellLink"/>'s, through an interface of their own: thermi sends each icon's picture, its menu and the
/// hotkeys' accelerators, and hears an icon's press, a menu's choice and a hotkey's press.
/// </summary>
internal sealed class ShellDesktop : IDisposable
{
    private const string Shell = "org.gnome.Shell";
    private const string Interface = "io.github.claudiopi.thermi.Desktop";

    // Each signal's shape in this protocol, as desktop.js declares it.
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        ["IconPressed"] = "(suu)",
        ["IconKey"] = "(ss)",
        ["IconChosen"] = "(si)",
        ["Hotkey"] = "(u)",
    };

    private readonly Gio.DBusConnection _bus;
    private readonly Gio.DBusProxy _signals;
    private readonly ILogger _log;

    public ShellDesktop(Gio.DBusConnection bus, ILogger log)
    {
        _bus = bus;
        _log = log;
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart;
        _signals = Gio.DBusProxy.NewSync(bus, Flags, null, Shell, ShellLink.ObjectPath, Interface, null);
        _signals.OnGSignal += (_, heard) => Heard(heard.SignalName, heard.Parameters);
    }

    /// <summary>An icon pressed, by its key: its button, and when, in milliseconds.</summary>
    public Action<string, uint, uint> IconPressed { get; set; } = (_, _, _) => { };

    /// <summary>A key pressed on an icon the top bar's focus is on, by the icon's key and the key's name, as "Return" or "Menu".</summary>
    public Action<string, string> IconKey { get; set; } = (_, _) => { };

    /// <summary>A row of an icon's menu chosen, by its place in the rows the menu was shown with.</summary>
    public Action<string, int> IconChosen { get; set; } = (_, _) => { };

    /// <summary>A hotkey pressed, by its place in the accelerators last taken.</summary>
    public Action<uint> Hotkey { get; set; } = _ => { };

    /// <summary>Shows the icons, in their order, in place of those shown; those not redrawn keep their pictures.</summary>
    public Task Icons(IReadOnlyList<TrayPicture> icons) =>
        Own("Icons", GLib.Variant.NewArray(GLib.VariantType.New("(ssayiiid)"), [.. icons.Select(IconOf)]));

    /// <summary>Shows the menu of <paramref name="rows"/> under the icon of <paramref name="key"/>.</summary>
    public Task IconMenu(string key, IReadOnlyList<ShellRow> rows) =>
        Own("IconMenu", GLib.Variant.NewString(key), ShellLink.RowsOf(rows));

    /// <summary>Takes the accelerators, an empty one for none, and says which are thermi's now.</summary>
    public async Task<IReadOnlyList<bool>> Hotkeys(IReadOnlyList<string> accelerators)
    {
        var taken = (await Own("Hotkeys", GLib.Variant.NewStrv([.. accelerators]))).GetChildValue(0);
        return [.. Enumerable.Range(0, (int)taken.NChildren()).Select(at => taken.GetChildValue((nuint)at).GetBoolean())];
    }

    public void Dispose() => _signals.Dispose();

    // The call carries a copy of the pixels, so the picture may change while it is on its way. An icon not redrawn goes
    // with no picture, and size 0, which keeps the one the extension shows: an empty array, as GirCore makes no empty bytes.
    private static GLib.Variant IconOf(TrayPicture icon)
    {
        var picture = icon.Picture;
        var (width, height, stride) = icon.Redrawn ? (picture.Width, picture.Height, picture.Stride) : (0, 0, 0);
        return GLib.Variant.NewTuple(
        [
            GLib.Variant.NewString(icon.Key),
            GLib.Variant.NewString(icon.Tooltip),
            icon.Redrawn ? PixelsOf(picture) : GLib.Variant.NewArray(GLib.VariantType.New("y"), []),
            GLib.Variant.NewInt32(width),
            GLib.Variant.NewInt32(height),
            GLib.Variant.NewInt32(stride),
            GLib.Variant.NewDouble(icon.Scale),
        ]);
    }

    private static GLib.Variant PixelsOf(Picture picture)
    {
        using var pixels = picture.Bytes();
        return GLib.Variant.NewFromBytes(GLib.VariantType.New("ay"), pixels, true);
    }

    private Task<GLib.Variant> Own(string method, params GLib.Variant[] args) =>
        _bus.CallAsync(Shell, ShellLink.ObjectPath, Interface, method, GLib.Variant.NewTuple(args));

    private void Heard(string signal, GLib.Variant args) => Callbacks.Safely(() => Take(signal, args), ex => _log.ShellSignalFailed(signal, ex));

    private void Take(string signal, GLib.Variant args)
    {
        if (!Shapes.TryGetValue(signal, out var shape) || args.GetTypeString() != shape)
        {
            _log.ShellSignalRefused(signal, args.GetTypeString());
            return;
        }

        var first = args.GetChildValue(0);
        switch (signal)
        {
            case "IconPressed":
                IconPressed(first.GetString(out _), args.GetChildValue(1).GetUint32(), args.GetChildValue(2).GetUint32());
                break;
            case "IconKey":
                IconKey(first.GetString(out _), args.GetChildValue(1).GetString(out _));
                break;
            case "IconChosen":
                IconChosen(first.GetString(out _), args.GetChildValue(1).GetInt32());
                break;
            default:
                Hotkey(first.GetUint32());
                break;
        }
    }
}
