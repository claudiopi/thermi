using System.Drawing;
using System.Runtime.InteropServices;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Overlay;

/// <summary>
/// An overlay's value tooltips: Windows' own tooltip control, one tool for each item where the painter drew it, its text
/// changed only when it changes, so a tooltip open over a value follows it. The control watches the overlay's mouse
/// itself, so a click-through overlay, which gets none, shows none.
/// </summary>
internal sealed class ValueTips : IDisposable
{
    /// <summary>The timer that wakes the overlay when the quiet after a drop may end.</summary>
    internal const nuint QuietTimer = 4;

    /// <summary>How long after a drop no tooltip shows while the pointer stays on the overlay.</summary>
    public static readonly TimeSpan AfterDrop = TimeSpan.FromSeconds(1.5);

    // Wide enough for a name and its value on one line, so only the tooltip's own lines break it.
    private const int MaxWidth = 480;

    // comctl32 version 5, which a process without its manifest loads, refuses version 6's longer structure.
    private static readonly uint ToolSize = (uint)Marshal.OffsetOf<TTTOOLINFOW>(nameof(TTTOOLINFOW.lpReserved));

    // Between the tooltip and the overlay, so it reads as the overlay's and covers none of its pills.
    private const int Gap = 6;

    // Room around the text at 100 %, as Windows 11's tooltips keep.
    private const int MarginAcross = 6;
    private const int MarginDown = 4;

    private readonly HWND _owner;
    private readonly HWND _tips;
    private readonly List<string> _texts = [];

    // Kept alive as long as the tooltip, which calls it.
    private readonly SUBCLASSPROC _placed;
    private bool? _dark;

    // When the last drop let go, on the injected clock, while its quiet lasts.
    private long? _dropped;

    public unsafe ValueTips(HWND owner)
    {
        _owner = owner;
        var controls = new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = INITCOMMONCONTROLSEX_ICC.ICC_WIN95_CLASSES };
        PInvoke.InitCommonControlsEx(controls);
        fixed (char* name = "tooltips_class32")
        {
            _tips = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOPMOST,
                name,
                null,
                WINDOW_STYLE.WS_POPUP | (WINDOW_STYLE)(PInvoke.TTS_ALWAYSTIP | PInvoke.TTS_NOPREFIX),
                0,
                0,
                0,
                0,
                owner,
                HMENU.Null,
                HiddenWindow.Instance,
                null);
        }

        PInvoke.SendMessage(_tips, PInvoke.TTM_SETMAXTIPWIDTH, 0, MaxWidth);
        _placed = Placed;
        if (!_tips.IsNull)
        {
            PInvoke.SetWindowSubclass(_tips, _placed, 0, 0);

            // Rounded and roomy as Windows 11's own flyouts are, not as the common control's tight box; Windows 10 refuses
            // the corners and keeps them square.
            var corners = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            _ = PInvoke.DwmSetWindowAttribute(_tips, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &corners, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
            Margins(PInvoke.GetDpiForWindow(owner));
        }
    }

    // In the overlay's pixels, which follow its monitor's scale.
    private unsafe void Margins(uint dpi)
    {
        var scale = dpi == 0 ? 1 : dpi / 96.0;
        var (across, down) = ((int)Math.Round(MarginAcross * scale), (int)Math.Round(MarginDown * scale));
        var margins = new RECT(across, down, across, down);
        PInvoke.SendMessage(_tips, PInvoke.TTM_SETMARGIN, 0, (nint)(&margins));
    }

    /// <summary>One tool per place, in the overlay's pixels, with its text; tools past the places go.</summary>
    public void Update(IReadOnlyList<(RectangleF Place, string Text)> tips)
    {
        if (_tips.IsNull)
        {
            return;
        }

        for (var at = 0; at < tips.Count; at++)
        {
            var (place, text) = tips[at];
            if (at >= _texts.Count)
            {
                Send(PInvoke.TTM_ADDTOOLW, at, place, text);
                _texts.Add(text);
                continue;
            }

            Send(PInvoke.TTM_NEWTOOLRECTW, at, place, null);
            if (_texts[at] != text)
            {
                Send(PInvoke.TTM_UPDATETIPTEXTW, at, place, text);
                _texts[at] = text;
            }
        }

        while (_texts.Count > tips.Count)
        {
            Send(PInvoke.TTM_DELTOOLW, _texts.Count - 1, default, null);
            _texts.RemoveAt(_texts.Count - 1);
        }
    }

    /// <summary>
    /// Draws the tooltips dark or light, as thermi's menus follow the apps' mode; Windows' tooltip turns dark only through
    /// Explorer's dark theme, and without a theme, as under high contrast, it takes the system's colors.
    /// </summary>
    public void Theme(bool dark)
    {
        if (_tips.IsNull || _dark == dark)
        {
            return;
        }

        _ = PInvoke.SetWindowTheme(_tips, dark ? "DarkMode_Explorer" : "Explorer", null);
        _dark = dark;
    }

    /// <summary>Hides the tooltip and shows none while the overlay is dragged or its menu shows, which it would cover.</summary>
    public void Quiet(bool quiet)
    {
        if (!_tips.IsNull)
        {
            PInvoke.SendMessage(_tips, PInvoke.TTM_ACTIVATE, quiet ? 0u : 1u, 0);
        }
    }

    /// <summary>
    /// A drop leaves the pointer on a value, whose tooltip would show at once: none shows until the pointer leaves the
    /// overlay, or for <see cref="AfterDrop"/>.
    /// </summary>
    public unsafe void Dropped(TimeProvider time)
    {
        _dropped = time.GetTimestamp();
        var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = _owner };
        PInvoke.TrackMouseEvent(ref track);
        Wake(AfterDrop);
    }

    /// <summary>The quiet's timer woke the overlay: the tooltips show again once the clock says it has lasted, or it waits on.</summary>
    public void Woke(TimeProvider time)
    {
        PInvoke.KillTimer(_owner, QuietTimer);
        if (_dropped is { } at && time.GetElapsedTime(at) < AfterDrop)
        {
            Wake(AfterDrop - time.GetElapsedTime(at));
            return;
        }

        Speak();
    }

    public void Left()
    {
        if (_dropped is not null)
        {
            PInvoke.KillTimer(_owner, QuietTimer);
            Speak();
        }
    }

    private void Speak()
    {
        _dropped = null;
        Quiet(false);
    }

    private void Wake(TimeSpan after) => PInvoke.SetTimer(_owner, QuietTimer, (uint)Math.Ceiling(after.TotalMilliseconds), null);

    public void Dispose()
    {
        PInvoke.KillTimer(_owner, QuietTimer);
        if (!_tips.IsNull)
        {
            PInvoke.RemoveWindowSubclass(_tips, _placed, 0);
            PInvoke.DestroyWindow(_tips);
        }
    }

    // Windows puts a tooltip under the pointer, or over it near the screen's foot, where it would cover the next pills: it
    // goes whole above the overlay instead, or below it where the screen has no room above, as on a taskbar at the top.
    private unsafe LRESULT Placed(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (message == PInvoke.WM_WINDOWPOSCHANGING
            && (WINDOWPOS*)lParam.Value is var pos
            && (pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOMOVE) != SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            && PInvoke.GetWindowRect(_owner, out var overlay))
        {
            var height = (pos->flags & SET_WINDOW_POS_FLAGS.SWP_NOSIZE) != SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                ? pos->cy
                : (PInvoke.GetWindowRect(window, out var tip) ? tip.bottom - tip.top : 0);
            var above = overlay.top - Gap - height;
            pos->y = above >= ScreenTop(overlay) ? above : overlay.bottom + Gap;
        }

        return PInvoke.DefSubclassProc(window, message, wParam, lParam);
    }

    private static int ScreenTop(RECT overlay)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var monitor = PInvoke.MonitorFromRect(overlay, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return PInvoke.GetMonitorInfo(monitor, ref info) ? info.rcMonitor.top : int.MinValue;
    }

    // The control copies the text, so it is pinned only for the call.
    private unsafe void Send(uint message, int id, RectangleF place, string? text)
    {
        fixed (char* chars = text)
        {
            var tool = new TTTOOLINFOW
            {
                cbSize = ToolSize,
                uFlags = TOOLTIP_FLAGS.TTF_SUBCLASS,
                hwnd = _owner,
                uId = (nuint)id,
                rect = new RECT((int)place.Left, (int)place.Top, (int)Math.Ceiling(place.Right), (int)Math.Ceiling(place.Bottom)),
                lpszText = chars,
            };
            PInvoke.SendMessage(_tips, message, 0, (nint)(&tool));
        }
    }
}
