using Thermi.App.Logging;
using Thermi.Core.Pipe;
using Thermi.Linux.Desktop;
using Thermi.Linux.Hotkeys;
using Thermi.Linux.Overlay;
using Thermi.Linux.Tray;

namespace Thermi.Linux.Gnome;

/// <summary>
/// What GNOME's host shows beside its overlays: thermi's icons in the top bar and its hotkeys, both through the extension,
/// and its notices, the Shell's own notifications.
/// </summary>
internal sealed class GnomeDesk : IDisposable
{
    private readonly ShellDesktop _desktop;

    public GnomeDesk(
        Gio.DBusConnection bus, OverlayParts parts, OverlayMenu menu, DesktopNotices notices, TraceEvents trace, ISensorFeed sensors, bool firstStart)
    {
        _desktop = new ShellDesktop(bus, parts.Log);
        var tray = new TrayHost(parts.Settings, parts.Visibility, parts.RequestPaint)
        {
            Menu = (overlay, item) => menu.Rows(overlay, item, fromTray: true, sensors.State, sensors.Latest),
            Choose = menu.Choose,
            OpenSettings = parts.OpenSettings,
        };
        var keys = new HotkeyHost(parts.Settings, parts.Visibility, parts.History, trace, parts.Log) { RequestPaint = parts.RequestPaint };
#pragma warning disable CA2000 // The tray and the hotkeys own what they show through, and dispose it with themselves.
        Tray = new DesktopTray(new ShellIcons(_desktop, parts.Pointer, parts.Call), tray);
        Hotkeys = new DesktopHotkeys(new ShellHotkeys(_desktop), keys);
#pragma warning restore CA2000
        var told = new NoticeHost(parts.Settings, parts.Visibility, parts.Culture, parts.RequestPaint)
        {
            Settle = parts.Settle,
            OpenSettings = () => parts.OpenSettings(0, null),
        };
        Notices = new ThermiNotices(notices, told, parts.Time, firstStart);
    }

    public DesktopTray Tray { get; }

    public DesktopHotkeys Hotkeys { get; }

    public ThermiNotices Notices { get; }

    /// <summary>The extension came up, with none of thermi's icons or hotkeys: the next paint sends each again.</summary>
    public void Arrived()
    {
        Tray.Forget();
        Hotkeys.Forget();
    }

    public void Dispose()
    {
        Tray.Dispose();
        Hotkeys.Dispose();
        _desktop.Dispose();
    }
}
