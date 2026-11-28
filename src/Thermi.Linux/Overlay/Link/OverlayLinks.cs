using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Core.Look;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Window;

namespace Thermi.Linux.Overlay.Link;

/// <summary>
/// What links the running overlay to the rest of the session, past its surfaces: the settings window, whose messages it
/// hears and answers; a second start of thermi, which hands it the request to open the window, as Windows' does; the
/// desktop's look, which it follows as the settings portal says it changes, and on KDE Plasma as its config files do; and
/// systemd's watchdog, told each second.
/// </summary>
internal sealed partial class OverlayLinks : IDisposable
{
    private readonly Gio.DBusConnection _bus;
    private readonly OverlayParts _parts;
    private readonly Watchdog _watchdog;
    private readonly List<Gio.FileMonitor> _watches = [];
    private DesktopPortal? _portal;

    public OverlayLinks(Gio.DBusConnection bus, OverlayParts parts)
    {
        (_bus, _parts) = (bus, parts);
        _watchdog = new Watchdog(parts.Log);
    }

    /// <summary>
    /// Asks the overlay already running in this session to open the settings window, with the desktop's token this start
    /// was given, so it comes in front as one the launcher started; true once it took it.
    /// </summary>
    public static bool HandOver(Gio.DBusConnection bus, ILogger log)
    {
        try
        {
            using var token = GLib.Variant.NewTuple([GLib.Variant.NewString(LaunchTokens.Given() ?? "")]);
            using var _ = bus.CallSync(
                ShellLink.Name, OverlayObject.Path, OverlayObject.Name, OverlayObject.Open, token, null, Gio.DBusCallFlags.NoAutoStart, -1, null);
            return true;
        }
        catch (GLib.GException ex)
        {
            NotHandedOver(log, ex);
            return false;
        }
    }

    /// <summary>The overlay's loop runs: it looks for the window, follows the desktop's look, and tells the watchdog.</summary>
    public void Start(bool firstStart)
    {
        _parts.Window.Start(firstStart);
        _portal = new DesktopPortal(_bus, Looked, _parts.Log);
        Watch();
        Looked();
    }

    // KDE Plasma's panels change colour with its Plasma theme or its colour scheme, which its config files say as it writes
    // them, and the settings portal does not for the theme.
    private void Watch()
    {
        if (!DesktopLooks.OnPlasma())
        {
            return;
        }

        foreach (var file in new[] { "plasmarc", "kdeglobals" })
        {
            var monitor = Gio.FileHelper.NewForPath(Path.Combine(DesktopLooks.ConfigHome(), file)).MonitorFile(Gio.FileMonitorFlags.None, null);
            monitor.OnChanged += (_, _) => Looked();
            _watches.Add(monitor);
        }
    }

    /// <summary>The overlay's second: the watchdog hears its loop runs.</summary>
    public void Tick() => _watchdog.Tell();

    /// <summary>A message of the settings window's, as <see cref="OverlayObject"/> took it; one that does not read is dropped.</summary>
    public void Heard(string payload)
    {
        if (WindowObject.Read(payload) is not { } message)
        {
            Dropped(_parts.Log);
            return;
        }

        // The window's call returns first, so neither side sends inside the other's handler.
        if (_parts.Window.Heard(message))
        {
            _parts.Post(_parts.Window.SendHistory);
        }
    }

    /// <summary>A second start of thermi asks for the window: it opens on the first overlay, in front with the token it was given.</summary>
    public void Open(string token)
    {
        _parts.Channel.Token = token.Length > 0 ? token : null;
        _parts.Channel.Click = OverlayMenu.Now;
        _parts.Window.OpenWindow(0, null);
    }

    /// <summary>The window opened or closed: its marks and fades change.</summary>
    public void WindowChanged()
    {
        _parts.Window.LookForWindow();
        _parts.RequestPaint();
    }

    public void Dispose()
    {
        _portal?.Dispose();
        _watches.ForEach(watch => watch.Dispose());
        _watchdog.Dispose();
    }

    private void Looked()
    {
        var portal = _portal;
        var contrast = portal?.HighContrast == true ? DesktopLooks.Contrasted : null;
        var accent = contrast?.Highlight ?? RgbOf(portal?.Accent) ?? DesktopLooks.Accent(null);
        _parts.Look = new DesktopLook(contrast, accent, portal?.Scheme == true, DesktopLooks.LightPanels());
        _parts.RequestPaint();
    }

    private static Rgb? RgbOf(string? accent) =>
        accent is { Length: 7 } && accent[0] == '#' && int.TryParse(accent.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? new Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "The overlay that runs in this session did not take the request to open the settings window")]
    private static partial void NotHandedOver(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A message from the settings window could not be read: it is dropped")]
    private static partial void Dropped(ILogger logger);
}
