using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Gnome;

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
    /// <see cref="Name"/>, where the settings window activates <c>quit</c> through <c>org.gtk.Actions</c>; the
    /// <see cref="Overlay.OverlayObject"/> is below it.
    /// </summary>
    public const string ObjectPath = "/io/github/claudiopi/thermi";

    /// <summary>The extension's uuid, as extensions.gnome.org knows it.</summary>
    public const string Uuid = "thermi@claudiopi.github.io";

    /// <summary>
    /// The protocol's version, which the extension's metadata.json says as <c>thermi-protocol</c>: each one keeps every call
    /// and signal of the last in its shape, and may add others (<see cref="ShellFace"/>).
    /// </summary>
    public const uint Protocol = 4;

    public const string Shell = "org.gnome.Shell";
    public const string Interface = "io.github.claudiopi.thermi.Shell";

    private const string ExtensionsFace = "org.gnome.Shell.Extensions";
    private const int Timeout = 5000;

    // Each signal's shape in this protocol, as extension.js declares it; MonitorsChanged's is any ShellFace.PartsOf reads.
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        ["Ready"] = "(u)",
        ["Pointer"] = "(usdduuudd)",
        ["Key"] = "(us)",
        ["Chosen"] = "(ui)",
    };

    private readonly Gio.DBusConnection _bus;
    private readonly Gio.DBusProxy _signals;
    private readonly Gio.DBusProxy _extensions;
    private readonly ILogger _log;
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);

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

    /// <summary>What the running extension offers, as <see cref="State"/> last read it.</summary>
    public ShellFace Face { get; private set; } = ShellFace.None;

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
    /// Where the extension stands: its running object's protocol and <see cref="Face"/>, or else what the Shell says of it.
    /// The Shell's states are its own numbers: 1 is on, 6 installed and waiting to start, 7 and 8 turning off and on.
    /// </summary>
    public ShellExtensionState State()
    {
        try
        {
            var answer = _bus.CallSync(
                Shell, ObjectPath, "org.freedesktop.DBus.Properties", "Get",
                GLib.Variant.NewTuple([GLib.Variant.NewString(Interface), GLib.Variant.NewString("Protocol")]),
                null, Gio.DBusCallFlags.None, Timeout, null);
            var protocol = answer.GetChildValue(0).GetVariant().GetUint32();
            var xml = _bus.CallSync(
                Shell, ObjectPath, "org.freedesktop.DBus.Introspectable", "Introspect", GLib.Variant.NewTuple([]), null, Gio.DBusCallFlags.None, Timeout, null);
            Face = ShellFace.Of(xml.GetChildValue(0).GetString(out _));
            return !Face.Shows ? ShellExtensionState.OtherVersion : protocol < Protocol ? ShellExtensionState.Earlier : ShellExtensionState.Ours;
        }
        catch (GLib.GException)
        {
            // No object there: the extension is not running.
        }

        Face = ShellFace.None;

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
            return ShellExtensionState.Starting;
        }

        if (info.NChildren() == 0)
        {
            return OnDisk() ? ShellExtensionState.Unread : ShellExtensionState.Missing;
        }

        return info.LookupValue("state", GLib.VariantType.New("d"))?.GetDouble() is 1 or 6 or 8 ? ShellExtensionState.Starting : ShellExtensionState.Off;
    }

    /// <summary>
    /// Whether the extension's files are where the Shell looks for extensions as it starts, as it asks GLib: the person's
    /// data folder, then the system's.
    /// </summary>
    public static bool OnDisk() =>
        new[] { GLib.Functions.GetUserDataDir() }.Concat(GLib.Functions.GetSystemDataDirs())
            .Any(folder => File.Exists(Path.Combine(folder, "gnome-shell", "extensions", Uuid, "metadata.json")));

    /// <summary>
    /// Asks the Shell to install the extension from extensions.gnome.org, which GNOME confirms with the person itself; the
    /// answer is never awaited, as the person may take any time, and the extension's Ready says when it came.
    /// </summary>
    public void Install() => _ = InstallAsync();

    /// <summary>Asks the Shell to install the extension, as <see cref="Install"/> does, and waits for the person's answer.</summary>
    public Task<GLib.Variant> InstallAsync() => Unbounded("InstallRemoteExtension", GLib.Variant.NewString(Uuid));

    public Task<GLib.Variant> EnableAsync() => Extensions("EnableExtension", GLib.Variant.NewString(Uuid));

    /// <summary>Asks the Shell to fetch the updates of its extensions, which it applies at the next sign-in.</summary>
    public void CheckForUpdates() => _ = CheckForUpdatesAsync();

    public Task<GLib.Variant> CheckForUpdatesAsync() => Extensions("CheckForUpdates");

    private Task<GLib.Variant> Extensions(string method, params GLib.Variant[] args) => Call(ExtensionsFace, "/org/gnome/Shell", ExtensionsFace, method, args);

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

    /// <summary>Shows an overlay's picture at <paramref name="opacity"/>, from 0 to 1, as its fade near the pointer.</summary>
    public Task Fade(uint overlay, double opacity) => Own("Fade", GLib.Variant.NewUint32(overlay), GLib.Variant.NewDouble(opacity));

    /// <summary>
    /// Shows a menu of <paramref name="rows"/> as the Shell's own popup menu, against <paramref name="anchor"/>, a box of
    /// the stage, on its <paramref name="side"/>, or on the other where it does not fit there.
    /// </summary>
    public Task Menu(uint overlay, PillBox anchor, MenuSide side, IReadOnlyList<ShellRow> rows) =>
        Own(
            "Menu",
            GLib.Variant.NewUint32(overlay),
            GLib.Variant.NewDouble(anchor.Left),
            GLib.Variant.NewDouble(anchor.Top),
            GLib.Variant.NewDouble(anchor.Width),
            GLib.Variant.NewDouble(anchor.Height),
            GLib.Variant.NewString(side == MenuSide.Above ? "above" : "below"),
            ShellWire.RowsOf(rows));

    /// <summary>Shows the pointer as <paramref name="shape"/> over the overlay's boxes, by its CSS name.</summary>
    public Task Cursor(uint overlay, PointerShape shape) =>
        Own("Cursor", GLib.Variant.NewUint32(overlay), GLib.Variant.NewString(shape switch
        {
            PointerShape.Default => "default",
            PointerShape.NotAllowed => "not-allowed",
            PointerShape.Resize => "nwse-resize",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No pointer is that."),
        }));

    /// <summary>Puts <paramref name="png"/> on the clipboard as a picture.</summary>
    public async Task Copy(byte[] png)
    {
        using var bytes = GLib.Bytes.New(png);
        await Own("Copy", GLib.Variant.NewFromBytes(GLib.VariantType.New("ay"), bytes, true));
    }

    /// <summary>Has the extension tell the pointer anywhere while <paramref name="on"/>, as a Pointer of kind <c>near</c>.</summary>
    public Task Watch(bool on) => Own("Watch", GLib.Variant.NewBoolean(on));

    /// <summary>The token that lets the window it opens take the focus; GDK's where the extension has no such call, which may be empty.</summary>
    public async Task<string> ActivationAsync(uint time) =>
        Lacks("Activation", "(u)") ? await LaunchTokens.Of(time) : (await Own("Activation", GLib.Variant.NewUint32(time))).GetChildValue(0).GetString(out _);

    /// <summary>
    /// The Shell's monitors, from an extension the overlays show through alone: another's may answer in another shape, so
    /// it gives none. The window asks it of its own link, which no <see cref="State"/> may have read yet.
    /// </summary>
    public async Task<IReadOnlyList<ShellMonitor>> MonitorsAsync()
    {
        var xml = await Call(Shell, ObjectPath, "org.freedesktop.DBus.Introspectable", "Introspect");
        return ShellFace.Of(xml.GetChildValue(0).GetString(out _)).Shows ? ShellWire.MonitorsOf((await Own("Monitors")).GetChildValue(0)) : [];
    }

    public void Dispose()
    {
        _signals.Dispose();
        _extensions.Dispose();
    }

    /// <summary>Whether the running extension lacks <paramref name="method"/> in the <paramref name="shape"/> thermi sends it, once logged.</summary>
    public bool Lacks(string method, string shape) => Lacks("Shell", method, shape);

    public bool Lacks(string face, string method, string shape)
    {
        if (!Face.Lacks(face, method, shape))
        {
            return false;
        }

        lock (_missing)
        {
            if (_missing.Add($"{face}.{method}{shape}"))
            {
                _log.ShellCallMissing($"{face}.{method}{shape}");
            }
        }

        return true;
    }

    // A call the running extension lacks, as an earlier protocol's, is done without: it answers nothing.
    private Task<GLib.Variant> Own(string method, params GLib.Variant[] args)
    {
        var call = GLib.Variant.NewTuple(args);
        return Lacks(method, call.GetTypeString()) ? Task.FromResult(GLib.Variant.NewTuple([])) : _bus.CallAsync(Shell, ObjectPath, Interface, method, call);
    }

    private Task<GLib.Variant> Call(string destination, string path, string face, string method, params GLib.Variant[] args) =>
        _bus.CallAsync(destination, path, face, method, GLib.Variant.NewTuple(args));

    // GirCore's CallAsync gives a call D-Bus's default 25 s, which GNOME's prompt to install outlasts while the person
    // reads it: this call has no timeout, and waits on a thread of its own, as only the blocking call takes one.
    private Task<GLib.Variant> Unbounded(string method, params GLib.Variant[] args)
    {
        var call = GLib.Variant.NewTuple(args);
        return Task.Run(() =>
            _bus.CallSync(ExtensionsFace, "/org/gnome/Shell", ExtensionsFace, method, call, null, Gio.DBusCallFlags.None, int.MaxValue, null));
    }

    // An extension of another protocol may send any shape: a signal is read only in the shape this protocol gives it, and a
    // Ready of another shape is another protocol's, whose number this one cannot read.
    private void Heard(string signal, GLib.Variant args)
    {
        try
        {
            var type = args.GetTypeString();
            if (Shapes.TryGetValue(signal, out var shape) ? type == shape
                : signal == "MonitorsChanged" && ShellFace.PartsOf(type[1..^1]) >= ShellFace.FewestMonitorParts)
            {
                Read(signal, args);
            }
            else if (signal == "Ready")
            {
                Ready(0);
            }
            else
            {
                _log.ShellSignalRefused(signal, type);
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
                MonitorsChanged(ShellWire.MonitorsOf(args.GetChildValue(0)));
                break;
            case "Chosen":
                Chosen(Overlay(), args.GetChildValue(1).GetInt32());
                break;
            default:
                break;
        }
    }
}
