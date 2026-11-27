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
// tells at once; the tick asks again, which sees a screen saver Windows never tells of, and heals a word missed.
internal sealed partial class OverlayHost
{
    // The display's state as Windows gives it: off, on, or dimmed, which still shows.
    private const uint DisplayOff = 0;

    private IDisposable? _sightWatch;
    private NotificationState _state;
    private bool _locked;
    private long _lockedAt;
    private bool _dark;
    private long _darkAt;

    private bool Seen => !_locked && _state != NotificationState.Away && !_dark;

    private void WatchSight()
    {
        _sightWatch = _environment.Sight.Watch(Hwnd);
        _state = _environment.Sight.State();
    }

    private void ForgetSight() => _sightWatch?.Dispose();

    // Asked once a second, for whether anybody is there and for the full-screen rule, which every paint till the next tick
    // reads. A lock holds till its unlock, and a display off till it is back, unless the word was missed: a key or the
    // mouse since, and for a lock Windows no longer saying nobody is there. Unseen, no paint reads the window in front,
    // which "Notify me" waits on, and no overlay needs keeping on top; seen just now, they were raised as they were.
    private void LookAgain()
    {
        var seen = Seen;
        _state = _environment.Sight.State();
        var input = _environment.Sight.SinceInput();
        _locked = _locked && (_state == NotificationState.Away || input >= _actions.Time.GetElapsedTime(_lockedAt));
        _dark = _dark && input >= _actions.Time.GetElapsedTime(_darkAt);
        SightChanged(seen);
        if (!Seen)
        {
            FullScreen(_settings.Config, _desktop.Screens());
        }
        else if (seen)
        {
            Raise();
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
            // An unlock says somebody is there now, whatever Windows said at the last tick, which asks again.
            (_locked, _lockedAt) = (wParam == PInvoke.WTS_SESSION_LOCK, _actions.Time.GetTimestamp());
            _state = !_locked && _state == NotificationState.Away ? NotificationState.Normal : _state;
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
    // No answer of the last input counts as none, so it ends no display off or lock.
    public static SessionSight Real { get; } = new(window => new Registration(window), Desktop.UserState, () => Desktop.SinceInput() ?? TimeSpan.MaxValue);

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
