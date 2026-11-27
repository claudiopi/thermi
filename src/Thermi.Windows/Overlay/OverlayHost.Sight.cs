using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Overlay;

// Whether anybody can see the overlays: nobody while the session is locked or its display is off, as Windows tells the
// host's window each time either changes. Nobody seeing them, they paint nothing and their own frames rest, while the
// sensors are still read, so the history, the notices and the agents stay right; once somebody can, they show what they
// missed at once. Until Windows tells, somebody can, so a host that never hears paints as before. The session's display,
// not the console's, so a remote session that shows the overlays keeps painting them.
internal sealed partial class OverlayHost
{
    // The display's state as Windows gives it: off, on, or dimmed, which still shows.
    private const uint DisplayOff = 0;

    private HPOWERNOTIFY _display;
    private bool _locked;
    private bool _dark;

    private bool Seen => !_locked && !_dark;

    // Windows answers at once with the display's state now, and then each time it changes.
    private unsafe void WatchSight()
    {
        PInvoke.WTSRegisterSessionNotification(Hwnd, PInvoke.NOTIFY_FOR_THIS_SESSION);
        var display = PInvoke.GUID_SESSION_DISPLAY_STATUS;
        _display = PInvoke.RegisterPowerSettingNotification(new HANDLE(Hwnd.Value), &display, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    private void ForgetSight()
    {
        PInvoke.UnregisterPowerSettingNotification(_display);
        PInvoke.WTSUnRegisterSessionNotification(Hwnd);
    }

    // True when the message said the session locked or unlocked, or its display went off or came back.
    private bool Heard(uint message, nuint wParam, nint lParam)
    {
        if (message == PInvoke.WM_WTSSESSION_CHANGE && (wParam == PInvoke.WTS_SESSION_LOCK || wParam == PInvoke.WTS_SESSION_UNLOCK))
        {
            _locked = wParam == PInvoke.WTS_SESSION_LOCK;
        }
        else if (message == PInvoke.WM_POWERBROADCAST && wParam == PInvoke.PBT_POWERSETTINGCHANGE && DisplayState(lParam) is { } state)
        {
            _dark = state == DisplayOff;
        }
        else
        {
            return false;
        }

        if (Seen)
        {
            Raise();
            RequestPaint();
        }
        else
        {
            _overlays.ForEach(overlay => overlay.Rest());
        }

        return true;
    }

    private static unsafe uint? DisplayState(nint lParam)
    {
        var setting = (POWERBROADCAST_SETTING*)lParam;
        return setting is not null && setting->PowerSetting == PInvoke.GUID_SESSION_DISPLAY_STATUS && setting->DataLength >= sizeof(uint)
            ? *(uint*)&setting->Data
            : null;
    }
}
