using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Logging;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using OverlayScene = Thermi.Overlay.OverlayScene;

namespace Thermi.Tray;

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
        if (_notices.Update(new NoticeInput(scene.Config, parts.Visibility.HideOverlays, state.WindowOpen, state.FirstStart)) is { } notice)
        {
            Tell(notice, scene.Config);
        }
    }

    /// <summary>Explorer made a new notification area: every icon goes back.</summary>
    public void Restore() => _icons.Restore();

    /// <summary>"Notify me when critical": a fresh snapshot, once a second, and the notice of each item critical long enough.</summary>
    public void Critical(ThermiConfig config, Snapshot snapshot, bool fullScreen)
    {
        var texts = Translations;
        var numbers = Actions.Culture;
        foreach (var (item, reading, notice) in _notifyMe.Add(config, snapshot, fullScreen))
        {
            var name = item.Name
                ?? (Metrics.Find(item.Metric ?? item.Auto) is { } metric ? texts.Of(metric.Name) : null)
                ?? OverlayLayout.LabelOf(item, texts)
                ?? item.Id;
            var value = ReadingFormatter.Format(reading with { Value = notice.Value }, numbers, units: true, config.Temperature, item.Decimals, texts);
            var length = LengthOf(notice.Length, texts, numbers);
            _icons.Notify(
                ThermiId,
                texts.Format(numbers, notice.WhilePlaying ? HostText.CriticalWhilePlayingTitle : HostText.CriticalTitle, name),
                notice.WhilePlaying
                    ? texts.Format(numbers, HostText.CriticalWhilePlaying, name, value, length)
                    : texts.Format(numbers, HostText.Critical, value, length));
        }
    }

    /// <summary>A callback of an icon: the menus, thermi's icon's click or Enter, a double-click, or a click on a notice.</summary>
    public void Event(uint message, uint what)
    {
        if (_icons.Of(message) is not { } icon)
        {
            return;
        }

        var config = Settings.Config;
        if (icon.ItemId is null && what is PInvoke.NIN_SELECT or KeySelect)
        {
            if (parts.Visibility.ClickIcon())
            {
                Actions.RequestPaint();
            }
            else
            {
                Menu(null);
            }
        }
        else if (what == PInvoke.WM_CONTEXTMENU)
        {
            Menu(icon.ItemId is null ? null : TrayScene.OverlayOf(config, icon.ItemId), fromTray: true);
        }
        else if (what == PInvoke.WM_LBUTTONDBLCLK && icon.ItemId is not null)
        {
            Actions.OpenSettings(TrayScene.OverlayOf(config, icon.ItemId), icon.ItemId);
        }
        else if (what == PInvoke.NIN_BALLOONUSERCLICK && icon.ItemId is null && _notices.Showing is { } notice)
        {
            Clicked(notice);
        }
    }

    /// <summary>
    /// Shows thermi's menu, or with <paramref name="overlay"/> that overlay's, from the overlay or, <paramref name="fromTray"/>,
    /// from one of its items' icons, and does what is chosen.
    /// </summary>
    public void Menu(int? overlay, bool fromTray = false)
    {
        if (overlay >= Math.Min(Settings.Config.Overlays.Count, parts.Overlays.Count))
        {
            return;
        }

        var (rows, place) = RowsOf(overlay, fromTray);
        if (ThermiMenus.Show(rows, place, overlay is { } owner ? parts.Overlays[owner].Hwnd : parts.Hwnd) is { } chosen)
        {
            Choose(chosen, overlay);
        }
    }

    public void Dispose() => _icons.Dispose();

    /// <summary>The rows of thermi's menu, or with <paramref name="overlay"/> that overlay's, and where they show.</summary>
    internal (IReadOnlyList<MenuRow> Rows, MenuPlace Place) RowsOf(int? overlay, bool fromTray = false)
    {
        var config = Settings.Config;
        var undo = Actions.Gestures?.Offered;
        var state = new MenuState(
            parts.Visibility.HideOverlays,
            Settings.Problem is not null,
            parts.Service(),
            undo is null ? null : TimeSpan.Zero,
            [.. Looks().Select(style => style.Name).OfType<string>()],
            UpdateReady: false);
        var texts = Translations;
        var rows = overlay is { } shown ? Menus.OfOverlay(config, shown, state, fromTray) : Menus.Thermis(config, state);
        return (rows, new MenuPlace(config, texts, Actions.Culture, overlay) { UndoText = undo is { } offered ? texts.Of(offered) : null });
    }

    /// <summary>A row chosen: in an overlay's menu, or an overlay's submenu, it acts on that overlay; in thermi's menu, on all.</summary>
    internal void Choose(MenuRow row, int? menuOverlay)
    {
        var target = row.Overlay ?? menuOverlay;
        IReadOnlyList<int> on = target is { } one ? [one] : [.. Enumerable.Range(0, Math.Min(Settings.Config.Overlays.Count, parts.Overlays.Count))];
        switch (row.Entry)
        {
            case MenuEntry.SettingsBroken or MenuEntry.Settings:
                Actions.OpenSettings(target ?? 0, null);
                break;
            case MenuEntry.StartSensors or MenuEntry.TaskManager or MenuEntry.CopyImage:
                Reach(row.Entry, target ?? 0);
                break;
            case MenuEntry.ShowOverlays or MenuEntry.HideOverlays:
                parts.Visibility.HideOverlays = row.Entry == MenuEntry.HideOverlays;
                break;
            case MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay:
                Settings.Edit(config => on.Aggregate(config, (current, at) =>
                    OverlayEdits.SetHidden(current, at, row.Entry == MenuEntry.HideThisOverlay).Config));
                break;
            case MenuEntry.UndoGesture:
                parts.Overlays[target ?? 0].UndoGesture();
                break;
            case MenuEntry.ClickThrough:
                Settings.Edit(config => on.Aggregate(config, (current, at) => OverlayEdits.SetClickThrough(current, at, !row.Checked).Config));
                break;
            case MenuEntry.Lock:
                Settings.Edit(config => on.Aggregate(config, (current, at) => OverlayEdits.SetLocked(current, at, !row.Checked).Config));
                break;
            case MenuEntry.Vertical or MenuEntry.Look:
                Restyle(row, target ?? 0);
                break;
            case MenuEntry.ResetMinMax:
                on.ToList().ForEach(at => Actions.History.Reset(Settings.Config.Overlays[at]));
                break;
            case MenuEntry.Exit:
                Actions.Exit();
                break;
            case MenuEntry.Overlay or MenuEntry.Update:
            default:
                break;
        }

        Actions.RequestPaint();
    }

    // What lies outside thermi: the sensor service, Task Manager, or the clipboard with the overlay's picture.
    private void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.StartSensors)
        {
            _ = Task.Run(StartSensorsAsync, CancellationToken.None);
        }
        else if (entry == MenuEntry.TaskManager)
        {
            parts.Environment.TaskManager();
        }
        else if (overlay < parts.Overlays.Count && parts.Overlays[overlay].Picture() is { } picture)
        {
            using (picture)
            {
                parts.Environment.CopyImage(parts.Overlays[overlay].Hwnd, picture);
            }
        }
    }

    private void Restyle(MenuRow row, int at)
    {
        if (row.Entry == MenuEntry.Vertical)
        {
            var orientation = row.Checked ? Orientation.Horizontal : Orientation.Vertical;
            Settings.Edit(config => StyleEdits.Restyle(config, at, style => style with { Orientation = orientation }).Config);
        }
        else if ((row.Look is null ? new StyleConfig() : Looks().FirstOrDefault(look => look.Name == row.Look)) is { } look)
        {
            Settings.Edit(config => StyleEdits.Choose(config, at, look).Config);
        }
    }

    private IReadOnlyList<StyleConfig> Looks() => new SavedStyles(ConfigStore.StylesFolder(Settings.FilePath)).List();

    // Windows' notices from an icon have no buttons of their own: a click on one does what its first button says, and
    // closing it is "Got it".
    private void Tell(Notice notice, ThermiConfig config)
    {
        var texts = Translations;
        var numbers = Actions.Culture;
        var hotkey = config.Hotkeys?.Toggle;
        var one = notice.Overlays.Count == 1 ? ThermiMenus.NameOf(config, notice.Overlays[0], texts) : "";
        var (title, text) = notice.Kind switch
        {
            NoticeKind.FirstRun => (texts.Of(HostText.FirstRunTitle), texts.Of(HostText.FirstRun)),
            NoticeKind.OverlaysHidden when config.Overlays.Count == 1 => (texts.Format(numbers, HostText.OverlayHiddenTitle, one),
                hotkey is null ? texts.Of(HostText.OverlayHiddenByFlag) : texts.Format(numbers, HostText.OverlayHiddenByFlagHotkey, hotkey)),
            NoticeKind.OverlaysHidden => (texts.Of(HostText.OverlaysHiddenTitle),
                hotkey is null ? texts.Of(HostText.OverlaysHidden) : texts.Format(numbers, HostText.OverlaysHiddenHotkey, hotkey)),
            NoticeKind.OverlayHidden => (texts.Format(numbers, HostText.OverlayHiddenTitle, one), texts.Of(HostText.OverlayHidden)),
            NoticeKind.ClickThrough or NoticeKind.ClickThroughAll => (texts.Of(HostText.ClickThroughOnTitle), texts.Of(HostText.ClickThroughOnText)),
            _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Kind, null),
        };
        _icons.Notify(ThermiId, title, text);
    }

    private void Clicked(Notice notice)
    {
        switch (notice.Kind)
        {
            case NoticeKind.FirstRun:
                Actions.OpenSettings(0, null);
                break;
            case NoticeKind.OverlaysHidden:
                parts.Visibility.HideOverlays = false;
                break;
            case NoticeKind.OverlayHidden:
                Settings.Edit(config => notice.Overlays.Aggregate(config, (current, at) => OverlayEdits.SetHidden(current, at, hidden: false).Config));
                break;
            case NoticeKind.ClickThrough or NoticeKind.ClickThroughAll:
                Settings.Edit(config => Notices.Undo(config, notice));
                break;
            default:
                break;
        }

        Actions.RequestPaint();
    }

    // On a thread of its own: starting a process behind UAC blocks its caller until the prompt closes, and the host's thread
    // must keep painting. The pipe coming back is what shows the service ran.
    private async Task StartSensorsAsync()
    {
        try
        {
            if (await parts.Environment.StartSensors(parts.Environment.ExePath).ConfigureAwait(false) is { } code and not StartSensors.Started)
            {
                log.StartSensorsEnded(code);
            }
        }
#pragma warning disable CA1031 // A start that fails leaves the sensors stopped, which the menu still offers to fix.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.StartSensorsFailed(ex);
        }
    }

    private static string LengthOf(TimeSpan length, Texts texts, IFormatProvider numbers) =>
        length < TimeSpan.FromMinutes(1) ? texts.Format(numbers, HostText.Seconds, (int)Math.Round(length.TotalSeconds))
        : texts.Format(numbers, UnitText.Minutes, (int)Math.Round(length.TotalMinutes));
}
