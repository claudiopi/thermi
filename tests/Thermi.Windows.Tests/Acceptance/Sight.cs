using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Placement;
using Thermi.Windows.Overlay;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.System.Power;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>The session as a test has it: away or not, its last input, and the window in front.</summary>
internal sealed class Sight(FakeTimeProvider clock)
{
    private bool _away;
    private long? _inputAt;

    public ForegroundWindow? Front { get; set; }

    public HostEnvironment Of(HostEnvironment environment) =>
        environment with { Foreground = () => Front, Sight = FakeTray.Watched with { State = State, SinceInput = SinceInput } };

    private NotificationState State() => _away ? NotificationState.Away : NotificationState.Normal;

    private TimeSpan SinceInput() => _inputAt is { } at ? clock.GetElapsedTime(at) : TimeSpan.MaxValue;

    // Nobody sees the session: its screen saver starts, of which Windows says nothing, or its display goes off by a key
    // let go just after, or it locks, which Windows' notification state may not show.
    public void Leave(OverlayHost host, string how)
    {
        if (how == "screen saver")
        {
            _away = true;
            return;
        }

        var display = how.Contains("display", StringComparison.Ordinal);
        Tell(host, display ? "display off" : "locked", seen: false);
        _away = !display && !how.Contains("no sign", StringComparison.Ordinal);
        if (display)
        {
            clock.Advance(TimeSpan.FromMilliseconds(200));
            Input();
        }
    }

    // While nobody sees it: a key, which a lock Windows says nobody is behind ignores.
    public void Meanwhile(string how)
    {
        if (how.Contains("a key while", StringComparison.Ordinal))
        {
            Input();
        }
    }

    // Somebody sees it again: the screen saver ends, it unlocks, or a key comes with no word of a display back or an
    // unlock, once Windows no longer says nobody is there.
    public void Return(OverlayHost host, string how)
    {
        if (how == "screen saver")
        {
            _away = false;
        }
        else if (how.StartsWith("unlocked", StringComparison.Ordinal))
        {
            Tell(host, "locked", seen: true);
        }
        else
        {
            _away = false;
            Input();
        }
    }

    private void Input() => _inputAt = clock.GetTimestamp();

    // A window over the whole of the monitor the overlay snaps to, as a game's.
    public static ForegroundWindow FullScreen()
    {
        var screen = Desktop.Screens().First(each => each.Primary).Bounds;
        return new ForegroundWindow("Game", screen, Maximized: false, Minimized: false) { Monitor = screen };
    }

    // As Windows says it: a lock or unlock of the session, or the session's display off, dimmed or on.
    public unsafe void Tell(OverlayHost host, string unseen, bool seen)
    {
        if (unseen == "locked")
        {
            _away = !seen;
            PInvoke.SendMessage(host.Hwnd, PInvoke.WM_WTSSESSION_CHANGE, seen ? PInvoke.WTS_SESSION_UNLOCK : PInvoke.WTS_SESSION_LOCK, 0);
            return;
        }

        var buffer = stackalloc byte[sizeof(POWERBROADCAST_SETTING) + sizeof(uint)];
        var setting = (POWERBROADCAST_SETTING*)buffer;
        setting->PowerSetting = PInvoke.GUID_SESSION_DISPLAY_STATUS;
        setting->DataLength = sizeof(uint);
        *(uint*)&setting->Data = seen ? 1u : unseen == "display dimmed" ? 2u : 0u;
        PInvoke.SendMessage(host.Hwnd, PInvoke.WM_POWERBROADCAST, PInvoke.PBT_POWERSETTINGCHANGE, (nint)setting);
    }
}
