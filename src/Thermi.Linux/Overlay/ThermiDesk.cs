using System.Globalization;
using Thermi.App.Logging;
using Thermi.Core.Pipe;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Hotkeys;
using Thermi.Linux.Tray;
using Thermi.Linux.X11;

namespace Thermi.Linux.Overlay;

/// <summary>How thermi started: the surfaces its desktop has, and whether it found no settings, which it then writes.</summary>
internal sealed record ThermiStart(SurfaceKind Kind, bool FirstStart);

/// <summary>
/// What thermi shows beside its overlays, each the way its desktop offers: its icons in GNOME's top bar through the
/// extension, else in the tray as StatusNotifierItems; its hotkeys through the extension, X11's grab, or else the
/// GlobalShortcuts portal; its notices, the desktop's own notifications; and what it answers its settings window on the
/// session's bus, the <see cref="OverlayObject"/>.
/// </summary>
internal sealed class ThermiDesk : IDisposable
{
    private readonly ShellDesktop? _desktop;
    private readonly BusObjects _objects;

    public ThermiDesk(
        Gio.DBusConnection bus, OverlayParts parts, OverlayMenu menu, DesktopNotices notices, TraceEvents trace, ISensorFeed sensors, ThermiStart start)
    {
        var tray = new TrayHost(parts.Settings, parts.Visibility, parts.RequestPaint)
        {
            Menu = (overlay, item) => menu.Rows(overlay, item, fromTray: true, sensors.State, sensors.Latest),
            Choose = menu.Choose,
            OpenSettings = parts.OpenSettings,
        };
        var keys = new HotkeyHost(parts.Settings, parts.Visibility, parts.History, trace, parts.Log) { RequestPaint = parts.RequestPaint };
        _objects = new BusObjects(bus, parts.Post, parts.Log);
#pragma warning disable CA2000 // The tray and the hotkeys own what they show through, and dispose it with themselves.
        if (start.Kind == SurfaceKind.Shell)
        {
            _desktop = new ShellDesktop(bus, parts.Log);
            Tray = new DesktopTray(new ShellIcons(_desktop, parts.Pointer, parts.Call), tray);
            Hotkeys = new DesktopHotkeys(new ShellHotkeys(_desktop), keys);
        }
        else
        {
            Tray = new DesktopTray(new StatusNotifierIcons(bus, _objects, parts.Log), tray);
            IHotkeyGrab grab = start.Kind == SurfaceKind.X11
                ? new X11Hotkeys(parts.Log)
                : new PortalHotkeys(() => Texts.For(parts.Settings.Config.Language, CultureInfo.CurrentUICulture), parts.Log);
            Hotkeys = new DesktopHotkeys(grab, keys);
        }
#pragma warning restore CA2000
        var hotkeys = Hotkeys;
        _objects.Serve(ShellLink.Name, OverlayObject.Path, new OverlayObject(() => hotkeys.Status, () => State()));
        var told = new NoticeHost(parts.Settings, parts.Visibility, parts.Culture, parts.RequestPaint)
        {
            Settle = parts.Settle,
            OpenSettings = () => parts.OpenSettings(0, null),
        };
        Notices = new ThermiNotices(notices, told, parts.Time, start.FirstStart);
    }

    public DesktopTray Tray { get; }

    public DesktopHotkeys Hotkeys { get; }

    public ThermiNotices Notices { get; }

    /// <summary>What the overlay tells agents now, as its <see cref="OverlayObject"/> answers it.</summary>
    public Func<OverlayState> State { get; init; } = () => new OverlayState(false, []);

    /// <summary>
    /// The surfaces came up, GNOME's extension again with none of thermi's icons or hotkeys: the next paint sends each again.
    /// A tray and the other grabs keep theirs.
    /// </summary>
    public void Arrived()
    {
        if (_desktop is not null)
        {
            Tray.Forget();
            Hotkeys.Forget();
        }
    }

    public void Dispose()
    {
        Tray.Dispose();
        Hotkeys.Dispose();
        _desktop?.Dispose();
        _objects.Dispose();
    }
}
