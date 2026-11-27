using Microsoft.Extensions.Logging;
using Thermi.Core.Look;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Gnome;

/// <summary>
/// A monitor as the extension tells it, in the Shell's logical pixels: its top bar's height, 0 for none, and whether it
/// shows a full-screen window. Another desktop's surfaces tell it the same way, with its name.
/// </summary>
internal sealed record ShellMonitor(double X, double Y, double Width, double Height, double Scale, double Bar, bool Primary, bool FullScreen)
{
    /// <summary>The monitor's name, where GTK finds the monitors; on GNOME, which the extension does not tell, Mutter's display config names it.</summary>
    public string? Id { get; init; }

    /// <summary>The top bar's centre box, the clock, from its left edge to its right, on the stage; (0, 0) off the top bar.</summary>
    public (double Left, double Right) Centre { get; init; }

    /// <summary>The top bar's right box, the system menu, as <see cref="Centre"/>.</summary>
    public (double Left, double Right) End { get; init; }
}

/// <summary>
/// The pointer on an overlay's boxes, as the extension sends it: what it did, where on the stage, its button and keys
/// held, and when, in milliseconds.
/// </summary>
internal sealed record ShellPointer(uint Overlay, string Kind, double X, double Y, uint Button, uint Modifiers, uint Time, double Dx, double Dy);

/// <summary>
/// A picture as the extension takes it: premultiplied ARGB in the machine's byte order, rows <see cref="Stride"/> bytes
/// apart, at <see cref="Scale"/> pixels to a logical one, taking the pointer only in <see cref="Clicks"/>, boxes of it in
/// logical pixels.
/// </summary>
internal sealed record ShellPicture(GLib.Bytes Pixels, int Width, int Height, int Stride, double Scale, IReadOnlyList<PillBox> Clicks);

/// <summary>A row of a menu as the extension shows it: the row it is under, -1 at the top, and what it reads.</summary>
internal sealed record ShellRow(int Parent, string Text, string? Note, bool Checked, bool Grayed, bool Starts);

/// <summary>Where the extension stands for thermi: running with thermi's protocol, of another, or not running.</summary>
internal enum ExtensionState
{
    Ours,
    OtherVersion,
    Missing,
    Off,

    /// <summary>Installed and on, but not up yet, as while the Shell starts: its Ready signal says when.</summary>
    Starting,
}

/// <summary>
/// thermi's side of the protocol with its GNOME Shell extension (src/Thermi.Gnome), over the session's D-Bus: thermi owns
/// its name, sends each overlay's picture, place and click boxes, and hears the pointer, the monitors and the menu's
/// choices. The extension's object lives on the Shell's own connection, so it is called at <c>org.gnome.Shell</c>.
/// Its signals come on the GLib main loop of the thread that made the link.
/// </summary>
internal sealed class ShellLink : IDisposable
{
    /// <summary>
    /// The name thermi's overlay takes on the session's bus, one per session: thermi's own id is the settings window's, a
    /// GTK application.
    /// </summary>
    public const string Name = "io.github.claudiopi.thermi.Overlay";

    /// <summary>
    /// The path of thermi's objects: the extension's, on the Shell's connection, and the overlay's actions, on its own under
    /// <see cref="Name"/>, where the settings window activates <c>quit</c> through <c>org.gtk.Actions</c>.
    /// </summary>
    public const string ObjectPath = "/io/github/claudiopi/thermi";

    /// <summary>The extension's uuid, as extensions.gnome.org knows it.</summary>
    public const string Uuid = "thermi@claudiopi.github.io";

    /// <summary>The protocol's version, which the extension's metadata.json says as <c>thermi-protocol</c>.</summary>
    public const uint Protocol = 3;

    public const string Shell = "org.gnome.Shell";
    public const string Interface = "io.github.claudiopi.thermi.Shell";
    private const int Timeout = 5000;

    // Each signal's shape in this protocol, as extension.js declares it.
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        ["Ready"] = "(u)",
        ["Pointer"] = "(usdduuudd)",
        ["Key"] = "(us)",
        ["MonitorsChanged"] = "(a(ddddddbbdddd))",
        ["Chosen"] = "(ui)",
    };

    private readonly Gio.DBusConnection _bus;
    private readonly Gio.DBusProxy _signals;
    private readonly Gio.DBusProxy _extensions;
    private readonly ILogger _log;

    public ShellLink(Gio.DBusConnection bus, ILogger log)
    {
        _bus = bus;
        _log = log;
        // A proxy's signals come through GObject's own, which GirCore marshals, where a subscription's callback it does not.
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart;
        _signals = Gio.DBusProxy.NewSync(bus, Flags, null, Shell, ObjectPath, Interface, null);
        _signals.OnGSignal += (_, heard) => Heard(heard.SignalName, heard.Parameters);
        _extensions = Gio.DBusProxy.NewSync(bus, Flags, null, Shell, "/org/gnome/Shell", "org.gnome.Shell.Extensions", null);
        _extensions.OnGSignal += (_, heard) => Changed(heard.SignalName, heard.Parameters);
    }

    public Action<uint> Ready { get; set; } = _ => { };

    /// <summary>The extension stopped running, as GNOME stops every extension while the screen is locked; its Ready says when it is back.</summary>
    public Action Gone { get; set; } = () => { };

    /// <summary>The Shell changed the extension's state: installed, removed, turned on or off.</summary>
    public Action StateChanged { get; set; } = () => { };

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    /// <summary>A key pressed while the pointer held an overlay, by its name, as "Escape".</summary>
    public Action<uint, string> Key { get; set; } = (_, _) => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; } = _ => { };

    /// <summary>A menu's row chosen, by its place in the rows the menu was shown with.</summary>
    public Action<uint, int> Chosen { get; set; } = (_, _) => { };

    /// <summary>Takes thermi's name on the bus; false when another overlay of the session holds it.</summary>
    public bool Own()
    {
        const uint DoNotQueue = 4;
        const uint Primary = 1;
        var answer = _bus.CallSync(
            "org.freedesktop.DBus",
            "/org/freedesktop/DBus",
            "org.freedesktop.DBus",
            "RequestName",
            GLib.Variant.NewTuple([GLib.Variant.NewString(Name), GLib.Variant.NewUint32(DoNotQueue)]),
            null,
            Gio.DBusCallFlags.None,
            Timeout,
            null);
        return answer.GetChildValue(0).GetUint32() == Primary;
    }

    /// <summary>
    /// Where the extension stands: its running object's protocol, or else what the Shell says of it. The Shell's states
    /// are its own numbers: 1 is on, 6 installed and waiting to start, 7 and 8 turning off and on.
    /// </summary>
    public ExtensionState State()
    {
        try
        {
            var answer = _bus.CallSync(
                Shell, ObjectPath, "org.freedesktop.DBus.Properties", "Get",
                GLib.Variant.NewTuple([GLib.Variant.NewString(Interface), GLib.Variant.NewString("Protocol")]),
                null, Gio.DBusCallFlags.None, Timeout, null);
            return answer.GetChildValue(0).GetVariant().GetUint32() == Protocol ? ExtensionState.Ours : ExtensionState.OtherVersion;
        }
        catch (GLib.GException)
        {
            // No object there: the extension is not running.
        }

        GLib.Variant info;
        try
        {
            info = _bus.CallSync(
                Shell, "/org/gnome/Shell", "org.gnome.Shell.Extensions", "GetExtensionInfo",
                GLib.Variant.NewTuple([GLib.Variant.NewString(Uuid)]), null, Gio.DBusCallFlags.None, Timeout, null).GetChildValue(0);
        }
        catch (GLib.GException)
        {
            // The Shell is not up yet, as at a sign-in that starts thermi first: the extension's Ready says when it is.
            return ExtensionState.Starting;
        }

        if (info.NChildren() == 0)
        {
            return ExtensionState.Missing;
        }

        return info.LookupValue("state", GLib.VariantType.New("d"))?.GetDouble() is 1 or 6 or 8 ? ExtensionState.Starting : ExtensionState.Off;
    }

    /// <summary>
    /// Asks the Shell to install the extension from extensions.gnome.org, which GNOME confirms with the person itself; the
    /// answer is never awaited, as the person may take any time, and the extension's Ready says when it came.
    /// </summary>
    public void Install() => _ = InstallAsync();

    /// <summary>Asks the Shell to install the extension, as <see cref="Install"/> does, and waits for the person's answer.</summary>
    public Task<GLib.Variant> InstallAsync() => Extensions("InstallRemoteExtension", GLib.Variant.NewString(Uuid));

    public Task<GLib.Variant> EnableAsync() => Extensions("EnableExtension", GLib.Variant.NewString(Uuid));

    /// <summary>Asks the Shell to fetch the updates of its extensions, which it applies at the next sign-in.</summary>
    public void CheckForUpdates() => _ = CheckForUpdatesAsync();

    public Task<GLib.Variant> CheckForUpdatesAsync() => Extensions("CheckForUpdates");

    private Task<GLib.Variant> Extensions(string method, params GLib.Variant[] args) =>
        Call("org.gnome.Shell.Extensions", "/org/gnome/Shell", "org.gnome.Shell.Extensions", method, args);

    /// <summary>Shows an overlay's picture with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>) of the stage.</summary>
    public async Task Show(uint overlay, ShellPicture picture, double x, double y)
    {
        var boxes = picture.Clicks.Select(box => GLib.Variant.NewTuple(
            [GLib.Variant.NewDouble(box.Left), GLib.Variant.NewDouble(box.Top), GLib.Variant.NewDouble(box.Width), GLib.Variant.NewDouble(box.Height)]));
        await Own(
            "Show",
            GLib.Variant.NewUint32(overlay),
            GLib.Variant.NewFromBytes(GLib.VariantType.New("ay"), picture.Pixels, true),
            GLib.Variant.NewInt32(picture.Width),
            GLib.Variant.NewInt32(picture.Height),
            GLib.Variant.NewInt32(picture.Stride),
            GLib.Variant.NewDouble(x),
            GLib.Variant.NewDouble(y),
            GLib.Variant.NewDouble(picture.Scale),
            GLib.Variant.NewArray(GLib.VariantType.New("(dddd)"), [.. boxes]));
    }

    public Task Move(uint overlay, double x, double y) => Own("Move", GLib.Variant.NewUint32(overlay), GLib.Variant.NewDouble(x), GLib.Variant.NewDouble(y));

    public Task Hide(uint overlay) => Own("Hide", GLib.Variant.NewUint32(overlay));

    /// <summary>Shows a menu of <paramref name="rows"/> at a point of the stage, as the Shell's own popup menu.</summary>
    public Task Menu(uint overlay, double x, double y, IReadOnlyList<ShellRow> rows) =>
        Own("Menu", GLib.Variant.NewUint32(overlay), GLib.Variant.NewDouble(x), GLib.Variant.NewDouble(y), RowsOf(rows));

    /// <summary>A menu's rows as the protocol carries them.</summary>
    public static GLib.Variant RowsOf(IReadOnlyList<ShellRow> rows) =>
        GLib.Variant.NewArray(GLib.VariantType.New("(issbbb)"), [.. rows.Select(row => GLib.Variant.NewTuple(
        [
            GLib.Variant.NewInt32(row.Parent),
            GLib.Variant.NewString(row.Text),
            GLib.Variant.NewString(row.Note ?? ""),
            GLib.Variant.NewBoolean(row.Checked),
            GLib.Variant.NewBoolean(row.Grayed),
            GLib.Variant.NewBoolean(row.Starts),
        ]))]);

    /// <summary>Has the extension tell the pointer anywhere while <paramref name="on"/>, as a Pointer of kind <c>near</c>.</summary>
    public Task Watch(bool on) => Own("Watch", GLib.Variant.NewBoolean(on));

    public async Task<IReadOnlyList<ShellMonitor>> MonitorsAsync() => MonitorsOf((await Own("Monitors")).GetChildValue(0));

    public void Dispose()
    {
        _signals.Dispose();
        _extensions.Dispose();
    }

    /// <summary>The monitors in the extension's <c>a(ddddddbbdddd)</c>, as its <c>Monitors</c> answers and its signal sends them.</summary>
    public static List<ShellMonitor> MonitorsOf(GLib.Variant monitors) =>
        [.. Enumerable.Range(0, (int)monitors.NChildren()).Select(at => monitors.GetChildValue((nuint)at)).Select(each => new ShellMonitor(
            each.GetChildValue(0).GetDouble(),
            each.GetChildValue(1).GetDouble(),
            each.GetChildValue(2).GetDouble(),
            each.GetChildValue(3).GetDouble(),
            each.GetChildValue(4).GetDouble(),
            each.GetChildValue(5).GetDouble(),
            each.GetChildValue(6).GetBoolean(),
            each.GetChildValue(7).GetBoolean())
        {
            Centre = (each.GetChildValue(8).GetDouble(), each.GetChildValue(9).GetDouble()),
            End = (each.GetChildValue(10).GetDouble(), each.GetChildValue(11).GetDouble()),
        })];

    private Task<GLib.Variant> Own(string method, params GLib.Variant[] args) => Call(Shell, ObjectPath, Interface, method, args);

    private Task<GLib.Variant> Call(string destination, string path, string face, string method, params GLib.Variant[] args) =>
        _bus.CallAsync(destination, path, face, method, GLib.Variant.NewTuple(args));

    // An extension of another protocol may send any shape: a signal is read only in the shape this protocol gives it, and a
    // Ready of another shape is another protocol's, whose number this one cannot read.
    private void Heard(string signal, GLib.Variant args)
    {
        try
        {
            if (Shapes.TryGetValue(signal, out var shape) && args.GetTypeString() == shape)
            {
                Read(signal, args);
            }
            else if (signal == "Ready")
            {
                Ready(0);
            }
            else
            {
                _log.ShellSignalRefused(signal, args.GetTypeString());
            }
        }
#pragma warning disable CA1031 // GLib calls it from native code, where an exception that escapes ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.ShellSignalFailed(signal, ex);
        }
    }

    // The Shell says each extension's state as it changes; thermi's is gone in any but 1, running.
    private void Changed(string signal, GLib.Variant args)
    {
        try
        {
            if (signal != "ExtensionStateChanged" || args.GetTypeString() != "(sa{sv})" || args.GetChildValue(0).GetString(out _) != Uuid)
            {
                return;
            }

            StateChanged();
            if (args.GetChildValue(1).LookupValue("state", GLib.VariantType.New("d"))?.GetDouble() is not 1)
            {
                Gone();
            }
        }
#pragma warning disable CA1031 // GLib calls it from native code, where an exception that escapes ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.ShellSignalFailed(signal, ex);
        }
    }

    private void Read(string signal, GLib.Variant args)
    {
        string Text(int at)
        {
            return args.GetChildValue((nuint)at).GetString(out _);
        }

        double Number(int at)
        {
            return args.GetChildValue((nuint)at).GetDouble();
        }

        uint Overlay()
        {
            return args.GetChildValue(0).GetUint32();
        }

        switch (signal)
        {
            case "Ready":
                Ready(Overlay());
                break;
            case "Pointer":
                Pointer(new ShellPointer(
                    Overlay(), Text(1), Number(2), Number(3), args.GetChildValue(4).GetUint32(), args.GetChildValue(5).GetUint32(),
                    args.GetChildValue(6).GetUint32(), Number(7), Number(8)));
                break;
            case "Key":
                Key(Overlay(), Text(1));
                break;
            case "MonitorsChanged":
                MonitorsChanged(MonitorsOf(args.GetChildValue(0)));
                break;
            case "Chosen":
                Chosen(Overlay(), args.GetChildValue(1).GetInt32());
                break;
            default:
                break;
        }
    }
}
