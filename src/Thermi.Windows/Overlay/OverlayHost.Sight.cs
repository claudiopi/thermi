using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Overlay;

// Nobody sees the overlays while the session is away, as Desktop.UserState says, or its display is off: they draw nothing and
// their own frames rest, while the sensors are still read, so the history, the notices and the agents stay right. Windows
// tells at once; the tick asks again, which sees a screen saver Windows never tells of, and heals a word missed: input
// since the display went off means it is back.
internal sealed partial class OverlayHost
{
    // The display's state as Windows gives it: off, on, or dimmed, which still shows.
    private const uint DisplayOff = 0;

    private IDisposable? _sightWatch;
    private NotificationState _state;
    private bool _away;
    private bool _dark;
    private long _darkAt;

    private bool Seen => !_away && !_dark;

    private void WatchSight()
    {
        _sightWatch = _environment.Sight.Watch(Hwnd);
        _state = _environment.Sight.State();
        _away = _state == NotificationState.Away;
    }

    private void ForgetSight() => _sightWatch?.Dispose();

    // Asked once a second, for whether anybody is there and for the full-screen rule, which every paint till the next tick
    // reads. Seen, the overlays are kept on top; unseen, no paint reads the window in front, which "Notify me" waits on,
    // and none needs keeping on top.
    private void LookAgain()
    {
        var seen = Seen;
        _state = _environment.Sight.State();
        var back = _dark && _environment.Sight.SinceInput() < _actions.Time.GetElapsedTime(_darkAt);
        (_away, _dark) = (_state == NotificationState.Away, _dark && !back);
        SightChanged(seen);
        if (Seen)
        {
            Raise();
        }
        else
        {
            FullScreen(_settings.Config, _desktop.Screens());
        }
    }

    private IReadOnlyList<bool> FullScreen(ThermiConfig config, IReadOnlyList<Screen> screens)
    {
        var fullScreen = OverlayVisibility.FullScreen(config, screens, _environment.Foreground(), _state);
        _fullScreen = fullScreen.Any(full => full);
        return fullScreen;
    }

    // True when the message said the session locked or unlocked, or its display went off, came back or dimmed.
    private bool Heard(uint message, nuint wParam, nint lParam)
    {
        var seen = Seen;
        if (message == PInvoke.WM_WTSSESSION_CHANGE && (wParam == PInvoke.WTS_SESSION_LOCK || wParam == PInvoke.WTS_SESSION_UNLOCK))
        {
            _away = wParam == PInvoke.WTS_SESSION_LOCK;
        }
        else if (message == PInvoke.WM_POWERBROADCAST && wParam == PInvoke.PBT_POWERSETTINGCHANGE && SessionSight.DisplayState(lParam) is { } state)
        {
            _dark = state == DisplayOff;
            _darkAt = _actions.Time.GetTimestamp();
        }
        else
        {
            return false;
        }

        SightChanged(seen);
        return true;
    }

    // Seen again, each overlay draws once, even with nothing new: the compositor may have dropped a layered window's
    // picture meanwhile, as a driver reset or a remote session's handover does.
    private void SightChanged(bool seen)
    {
        if (Seen == seen)
        {
            return;
        }

        if (Seen)
        {
            _overlays.ForEach(overlay => overlay.DrawAgain());
            Raise();
            RequestPaint();
        }
        else
        {
            _overlays.ForEach(overlay => overlay.Rest());
        }
    }
}

/// <summary>
/// How the host learns whether anybody can see the session: what Windows tells the window <see cref="Watch"/> registers as
/// the session locks or unlocks and its display goes off or on, what Windows says the user is doing now, away included,
/// and how long since the last input. The session's display, not the console's, so a remote session that shows the
/// overlays keeps drawing them.
/// </summary>
internal sealed record SessionSight(Func<HWND, IDisposable> Watch, Func<NotificationState> State, Func<TimeSpan> SinceInput)
{
    public static SessionSight Real { get; } = new(window => new Registration(window), Desktop.UserState, Desktop.SinceInput);

    /// <summary>The session display's state a power setting's message carries, or null for another setting's.</summary>
    public static unsafe uint? DisplayState(nint lParam)
    {
        var setting = (POWERBROADCAST_SETTING*)lParam;
        return setting is not null && setting->PowerSetting == PInvoke.GUID_SESSION_DISPLAY_STATUS && setting->DataLength >= sizeof(uint)
            ? *(uint*)&setting->Data
            : null;
    }

    // Windows answers the display's registration at once with its state now, and then each time it changes.
    private sealed unsafe class Registration : IDisposable
    {
        private readonly HWND _window;
        private readonly HPOWERNOTIFY _display;

        public Registration(HWND window)
        {
            _window = window;
            PInvoke.WTSRegisterSessionNotification(window, PInvoke.NOTIFY_FOR_THIS_SESSION);
            var display = PInvoke.GUID_SESSION_DISPLAY_STATUS;
            _display = PInvoke.RegisterPowerSettingNotification(new HANDLE(window.Value), &display, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
        }

        public void Dispose()
        {
            PInvoke.UnregisterPowerSettingNotification(_display);
            PInvoke.WTSUnRegisterSessionNotification(_window);
        }
    }
}
