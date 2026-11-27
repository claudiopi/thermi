using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using OverlayScene = Thermi.Windows.Overlay.OverlayScene;

namespace Thermi.Windows.Tray;

/// <summary>
/// What the tray refreshes from besides the host's own state: the scene painted, whether the window is open, and whether it
/// has not opened since thermi's first start.
/// </summary>
internal sealed record TrayState(OverlayScene Scene, bool WindowOpen, bool FirstStart);

/// <summary>What the tray reaches in the host: its window, settings, visibility, overlays and actions, and the sensor service's state.</summary>
internal sealed record TrayParts(
    HWND Hwnd,
    Settings Settings,
    OverlayVisibility Visibility,
    IReadOnlyList<OverlayWindow> Overlays,
    OverlayActions Actions,
    HostEnvironment Environment,
    Func<ServiceState> Service);

/// <summary>
/// thermi's icon and the items' icons, in the host's process: their refresh with thermi's mark, the notices from
/// thermi's icon (the first run, hidden overlays, click-through and "Notify me"), what a click or Enter on an icon does,
/// and thermi's menu and each overlay's, built from Core's <see cref="Menus"/>.
/// </summary>
internal sealed class ThermiTray(TrayParts parts, ILogger log) : IDisposable
{
    // Windows' NIN_KEYSELECT, NIN_SELECT with NINF_KEY, which CsWin32 does not carry: Enter or Space on the icon.
    private const uint KeySelect = PInvoke.NIN_SELECT | 1;

    private readonly TrayIcons _icons = new(parts.Hwnd, parts.Environment.Shell, parts.Actions.Time, log);
    private readonly Notices _notices = new();
    private readonly NotifyMe _notifyMe = new(parts.Actions.Time);
    private readonly TrayChoices _choices = new(parts, log);

    private Snapshot? _known;

    private Settings Settings => parts.Settings;

    private OverlayActions Actions => parts.Actions;

    private Guid ThermiId => TrayScene.IdOf(null, parts.Environment.ExePath);

    private Texts Translations => Texts.For(Settings.Config.Language, CultureInfo.CurrentUICulture);

    /// <summary>
    /// Shows the icons of <paramref name="state"/>, at the notification area's size, which is the primary monitor's, then
    /// the notice that is new, after the refresh, so thermi's icon is there to show it.
    /// </summary>
    public void Refresh(TrayState state)
    {
        var scene = state.Scene;
        _known = scene.Snapshot ?? _known;
        var input = new TrayInput(scene.Snapshot, parts.Service(), Actions.Culture, Translations, scene.LightTaskbar, parts.Environment.ExePath)
        {
            History = Actions.History,
            HideOverlays = parts.Visibility.HideOverlays,
            HighContrast = scene.HighContrast is not null,
        };
        var primary = scene.Screens.FirstOrDefault(screen => screen.Primary) ?? (scene.Screens.Count > 0 ? scene.Screens[0] : null);
        // Under high contrast the mark is the glyph in the system's text color, which only Windows knows.
        var icons = TrayScene.Of(scene.Config, input)
            .Select(icon => icon.Mark == MarkState.HighContrast && scene.HighContrast is { } system ? icon with { Color = system.WindowText } : icon)
            .ToList();
        _icons.Refresh(icons, TrayIcons.SizeAt((uint)Math.Round((primary?.Scale ?? 1) * 96)));

        // A change while the window is open is taken as the window's, whose list already says what is hidden.
        var driver = input.Service != ServiceState.Running ? false : scene.Snapshot is { } read ? SensorMap.DriverMayBeMissing(read) : (bool?)null;
        var told = new NoticeInput(scene.Config, parts.Visibility.HideOverlays, state.WindowOpen, state.FirstStart) { DriverMissing = driver };
        if (_notices.Update(told) is { } notice)
        {
            Tell(notice, scene.Config);
        }
    }

    /// <summary>Explorer made a new notification area: every icon goes back.</summary>
    public void Restore() => _icons.Restore();

    /// <summary>"Notify me when critical": a fresh snapshot, once a second, and the notice of each item critical long enough.</summary>
    public void Critical(ThermiConfig config, Snapshot snapshot, bool fullScreen)
    {
        foreach (var told in _notifyMe.Add(config, snapshot, fullScreen))
        {
            var (title, text) = NoticeTexts.Of(told, config, snapshot, Translations, Actions.Culture);
            _icons.Notify(ThermiId, title, text);
        }
    }

    /// <summary>A callback of an icon: the menus, thermi's icon's click or Enter, a double-click, or a click on a notice.</summary>
    public void Event(uint message, uint what)
    {
        if (_icons.Of(message) is not { } icon)
        {
            return;
        }

        TrayPress? press = what switch
        {
            PInvoke.NIN_SELECT or KeySelect => TrayPress.Activate,
            PInvoke.WM_CONTEXTMENU => TrayPress.Menu,
            PInvoke.WM_LBUTTONDBLCLK => TrayPress.Twice,
            _ => null,
        };
        if (press is { } pressed)
        {
            Answer(icon.ItemId, TrayPresses.Of(icon.ItemId, pressed, parts.Visibility));
        }
        else if (what == PInvoke.NIN_BALLOONUSERCLICK && icon.ItemId is null && _notices.Showing is { } notice)
        {
            Clicked(notice);
        }
    }

    /// <summary>
    /// Shows thermi's menu, or with <paramref name="overlay"/> that overlay's, on <paramref name="item"/> when one is named,
    /// and does what is chosen.
    /// </summary>
    public void Menu(int? overlay, bool fromTray = false, string? item = null)
    {
        if (overlay >= Math.Min(Settings.Config.Overlays.Count, parts.Overlays.Count))
        {
            return;
        }

        var (rows, place) = RowsOf(overlay, fromTray, item);
        if (ThermiMenus.Show(rows, place, overlay is { } owner ? parts.Overlays[owner].Hwnd : parts.Hwnd) is { } chosen)
        {
            Choose(chosen, overlay);
        }
    }

    public void Dispose() => _icons.Dispose();

    /// <summary>The rows of thermi's menu, or with <paramref name="overlay"/> that overlay's, and where they show.</summary>
    internal (IReadOnlyList<MenuRow> Rows, MenuPlace Place) RowsOf(int? overlay, bool fromTray = false, string? item = null)
    {
        var config = Settings.Config;
        var undo = Actions.Gestures?.Offered(config);
        var state = new MenuState(
            parts.Visibility.HideOverlays,
            Settings.Problem is not null,
            parts.Service(),
            undo is null ? null : TimeSpan.Zero,
            _choices.Looks(),
            UpdateReady: false);
        var texts = Translations;
        var rows = overlay is { } shown ? Menus.OfOverlay(config, shown, state, item, fromTray) : Menus.Thermis(config, state);
        return (rows, new MenuPlace(config, texts, Actions.Culture, overlay) { UndoText = undo is { } offered ? texts.Of(offered) : null, Known = _known });
    }

    /// <summary>A row chosen: in an overlay's menu, or an overlay's submenu, it acts on that overlay; in thermi's menu, on all.</summary>
    internal void Choose(MenuRow row, int? menuOverlay) => _choices.Choose(row, menuOverlay);

    // Windows' notices from an icon have no buttons of their own: a click on one does what its first button says, and
    // closing it is "Got it".
    private void Tell(Notice notice, ThermiConfig config)
    {
        var (title, text) = NoticeTexts.Of(notice, config, Translations, Actions.Culture);
        _icons.Notify(ThermiId, title, text);
    }

    private void Answer(string? item, TrayAnswer answer)
    {
        var overlay = TrayScene.OverlayOf(Settings.Config, item);
        switch (answer)
        {
            case TrayAnswer.Paint:
                Actions.RequestPaint();
                break;
            case TrayAnswer.Menu:
                Menu(item is null ? null : overlay, fromTray: true, item);
                break;
            case TrayAnswer.Settings:
                Actions.OpenSettings(overlay, item);
                break;
            case TrayAnswer.None:
            default:
                break;
        }
    }

    private void Clicked(Notice notice)
    {
        _ = NoticeChoices.Choose(notice, NoticeChoices.Clicked(notice), Settings, parts.Visibility, Actions.Settle, () => Actions.OpenSettings(0, null));
        Actions.RequestPaint();
    }
}
