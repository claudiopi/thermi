using Microsoft.Extensions.Logging;
using Thermi.Linux.Logging;
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

    /// <summary>An icon pressed, by its name: its button, and when, in milliseconds.</summary>
    public Action<string, uint, uint> IconPressed { get; set; } = (_, _, _) => { };

    /// <summary>A row of an icon's menu chosen, by its place in the rows the menu was shown with.</summary>
    public Action<string, int> IconChosen { get; set; } = (_, _) => { };

    /// <summary>A hotkey pressed, by its place in the accelerators last taken.</summary>
    public Action<uint> Hotkey { get; set; } = _ => { };

    /// <summary>Shows the icons, in their order, in place of those shown.</summary>
    public Task Icons(IReadOnlyList<TrayPicture> icons) =>
        Own("Icons", GLib.Variant.NewArray(GLib.VariantType.New("(ssayiiid)"), [.. icons.Select(IconOf)]));

    /// <summary>Shows the menu of <paramref name="rows"/> under the icon of <paramref name="name"/>.</summary>
    public Task IconMenu(string name, IReadOnlyList<ShellRow> rows) =>
        Own("IconMenu", GLib.Variant.NewString(name), ShellLink.RowsOf(rows));

    /// <summary>Takes the accelerators, an empty one for none, and says which are thermi's now.</summary>
    public async Task<IReadOnlyList<bool>> Hotkeys(IReadOnlyList<string> accelerators)
    {
        var taken = (await Own("Hotkeys", GLib.Variant.NewStrv([.. accelerators]))).GetChildValue(0);
        return [.. Enumerable.Range(0, (int)taken.NChildren()).Select(at => taken.GetChildValue((nuint)at).GetBoolean())];
    }

    public void Dispose() => _signals.Dispose();

    // The call carries a copy of the pixels, so the picture may change while it is on its way.
    private static GLib.Variant IconOf(TrayPicture icon)
    {
        using var pixels = icon.Picture.Bytes();
        var picture = icon.Picture;
        return GLib.Variant.NewTuple(
        [
            GLib.Variant.NewString(icon.Name),
            GLib.Variant.NewString(icon.Tooltip),
            GLib.Variant.NewFromBytes(GLib.VariantType.New("ay"), pixels, true),
            GLib.Variant.NewInt32(picture.Width),
            GLib.Variant.NewInt32(picture.Height),
            GLib.Variant.NewInt32(picture.Stride),
            GLib.Variant.NewDouble(icon.Scale),
        ]);
    }

    private Task<GLib.Variant> Own(string method, params GLib.Variant[] args) =>
        _bus.CallAsync(Shell, ShellLink.ObjectPath, Interface, method, GLib.Variant.NewTuple(args));

    private void Heard(string signal, GLib.Variant args)
    {
        try
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
                case "IconChosen":
                    IconChosen(first.GetString(out _), args.GetChildValue(1).GetInt32());
                    break;
                default:
                    Hotkey(first.GetUint32());
                    break;
            }
        }
#pragma warning disable CA1031 // GLib calls it from native code, where an exception that escapes ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.ShellSignalFailed(signal, ex);
        }
    }
}
