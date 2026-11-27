using System.Drawing;
using System.Runtime.InteropServices;
using Thermi.App.Gestures;
using Thermi.App.Overlay;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Windows.Painting;
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

    // Wide enough for a name and its value on one line, so only the tooltip's own lines break it.
    private const int MaxWidth = 480;

    // comctl32 version 5, which a process without its manifest loads, refuses version 6's longer structure.
    private static readonly uint ToolSize = (uint)Marshal.OffsetOf<TTTOOLINFOW>(nameof(TTTOOLINFOW.lpReserved));

    // Room around the text at 100 %, as Windows 11's tooltips keep.
    private const int MarginAcross = 6;
    private const int MarginDown = 4;

    private readonly HWND _owner;
    private readonly HWND _tips;
    private readonly List<string> _texts = [];
    private readonly List<RectangleF> _places = [];

    // A tooltip shows only with the pointer on a value, and making its text costs a reading of every sensor, so the texts
    // are made only while it is on one, and as it comes onto one, before the tooltip shows.
    private Func<int, string> _textOf = _ => "";
    private bool _on;

    // Kept alive as long as the tooltip, which calls it.
    private readonly SUBCLASSPROC _placed;
    private bool? _dark;

    // What holds the tooltips quiet, as every OS's overlay holds them.
    private readonly TipQuiet _quiet = new();

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

    /// <summary>
    /// One tool over each of the first <paramref name="count"/> items painted, in the overlay's pixels, each with the text
    /// <paramref name="textOf"/> makes; tools past them go.
    /// </summary>
    public void Update(IReadOnlyList<PaintedItem> painted, int count, Func<int, string> textOf)
    {
        if (_tips.IsNull)
        {
            return;
        }

        _textOf = textOf;
        count = Math.Min(count, painted.Count);
        for (var at = 0; at < count; at++)
        {
            var place = painted[at].Item.Rect();
            if (at >= _texts.Count)
            {
                Send(PInvoke.TTM_ADDTOOLW, at, place, "");
                _texts.Add("");
                _places.Add(place);
                continue;
            }

            Send(PInvoke.TTM_NEWTOOLRECTW, at, place, null);
            _places[at] = place;
        }

        while (_texts.Count > count)
        {
            Send(PInvoke.TTM_DELTOOLW, _texts.Count - 1, default, null);
            _texts.RemoveAt(_texts.Count - 1);
            _places.RemoveAt(_places.Count - 1);
        }

        Texts();
    }

    /// <summary>Whether the pointer is on a value; coming onto one, the texts are made now.</summary>
    public void On(bool on)
    {
        _on = on;
        Texts();
    }

    private void Texts()
    {
        for (var at = 0; _on && at < _texts.Count; at++)
        {
            var text = _textOf(at);
            if (_texts[at] != text)
            {
                Send(PInvoke.TTM_UPDATETIPTEXTW, at, _places[at], text);
                _texts[at] = text;
            }
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

    /// <summary>Whether a tooltip may show now.</summary>
    public bool Speaks => _quiet.Speaks;

    /// <summary>
    /// Hides the tooltip and shows none while the overlay is pressed or dragged, its menu shows, which it would cover, or a
    /// drop's quiet lasts, until none of them does.
    /// </summary>
    public void Quiet(Hush why, bool quiet)
    {
        _quiet.Quiet(why, quiet);
        Activate();
    }

    /// <summary>
    /// The overlay shows on screen after <paramref name="drop"/>: while its quiet lasts, a drop it has not yet heard of
    /// holds the tooltips until the pointer leaves the overlay, which Windows says at once of one the pointer is not on.
    /// </summary>
    public unsafe void After(DropQuiet drop)
    {
        if (_quiet.After(drop) is not { } wait)
        {
            return;
        }

        Activate();
        var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = _owner };
        PInvoke.TrackMouseEvent(ref track);
        Wake(wait);
    }

    /// <summary>The quiet's timer woke the overlay: the tooltips show again once the clock says it has lasted, or it waits on.</summary>
    public void Woke()
    {
        PInvoke.KillTimer(_owner, QuietTimer);
        if (_quiet.Woke() is { } left)
        {
            Wake(left);
            return;
        }

        Activate();
    }

    public void Left()
    {
        if (_quiet.Left())
        {
            PInvoke.KillTimer(_owner, QuietTimer);
            Activate();
        }
    }

    private void Activate()
    {
        if (!_tips.IsNull)
        {
            PInvoke.SendMessage(_tips, PInvoke.TTM_ACTIVATE, Speaks ? 1u : 0u, 0);
        }
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
    // goes whole above the overlay instead, or below it where the screen has no room above, as every OS's overlay places it.
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
            pos->y = OverlayTips.Top(new PixelRect(overlay.left, overlay.top, overlay.right, overlay.bottom), height, ScreenTop(overlay));
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
