using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Overlay;

// Whether anybody can see the overlays: nobody while the session is away, as Desktop.Locked says, or its display is off.
// Nobody seeing them, they draw nothing and their own frames rest, while the sensors are still read, so the history, the
// notices and the agents stay right; once somebody can, they show what they missed at once. Windows says at once when the
// session locks or unlocks and when its display goes off or on, the session's display, not the console's, so a remote
// session that shows the overlays keeps drawing them. The tick asks again, which also sees a screen saver, that Windows
// never says, and heals a word missed: input since the display went off means it is back.
internal sealed partial class OverlayHost
{
    // The display's state as Windows gives it: off, on, or dimmed, which still shows.
    private const uint DisplayOff = 0;

    private IDisposable? _sightWatch;
    private bool _away;
    private bool _dark;
    private long _darkAt;

    private bool Seen => !_away && !_dark;

    private void WatchSight()
    {
        _sightWatch = _environment.Sight.Watch(Hwnd);
        _away = _environment.Sight.Away();
    }

    private void ForgetSight() => _sightWatch?.Dispose();

    // Asked each second: while nobody sees the overlays no paint reads the window in front, which "Notify me" waits on.
    private void LookAgain()
    {
        var away = _environment.Sight.Away();
        var back = _dark && _environment.Sight.SinceInput() < _actions.Time.GetElapsedTime(_darkAt);
        if (away != _away || back)
        {
            (_away, _dark) = (away, _dark && !back);
            SightChanged();
        }

        if (!Seen)
        {
            FullScreen(_settings.Config, _desktop.Screens());
        }
    }

    private IReadOnlyList<bool> FullScreen(ThermiConfig config, IReadOnlyList<Screen> screens)
    {
        var fullScreen = OverlayVisibility.FullScreen(config, screens, _environment.Foreground(), Desktop.UserState());
        _fullScreen = fullScreen.Any(full => full);
        return fullScreen;
    }

    // True when the message said the session locked or unlocked, or its display went off, came back or dimmed.
    private bool Heard(uint message, nuint wParam, nint lParam)
    {
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

        SightChanged();
        return true;
    }

    private void SightChanged()
    {
        if (Seen)
        {
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
/// the session locks or unlocks and its display goes off or on, whether the session is away now, and how long since its
/// last input.
/// </summary>
internal sealed record SessionSight(Func<HWND, IDisposable> Watch, Func<bool> Away, Func<TimeSpan> SinceInput)
{
    public static SessionSight Real { get; } = new(window => new Registration(window), Desktop.Locked, Desktop.SinceInput);

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
