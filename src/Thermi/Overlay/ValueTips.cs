using System.Drawing;
using System.Runtime.InteropServices;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Overlay;

/// <summary>
/// An overlay's value tooltips: Windows' own tooltip control, one tool for each item where the painter drew it, its text
/// changed only when it changes, so a tooltip open over a value follows it. The control watches the overlay's mouse
/// itself, so a click-through overlay, which gets none, shows none.
/// </summary>
internal sealed class ValueTips : IDisposable
{
    // Wide enough for a name and its value on one line, so only the tooltip's own lines break it.
    private const int MaxWidth = 480;

    // comctl32 version 5, which a process without its manifest loads, refuses version 6's longer structure.
    private static readonly uint ToolSize = (uint)Marshal.OffsetOf<TTTOOLINFOW>(nameof(TTTOOLINFOW.lpReserved));

    private readonly HWND _owner;
    private readonly HWND _tips;
    private readonly List<string> _texts = [];
    private bool? _dark;

    public unsafe ValueTips(HWND owner)
    {
        _owner = owner;
        var controls = new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = INITCOMMONCONTROLSEX_ICC.ICC_WIN95_CLASSES };
        PInvoke.InitCommonControlsEx(controls);
        fixed (char* name = "tooltips_class32")
        {
            _tips = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOPMOST, name, null, WINDOW_STYLE.WS_POPUP | (WINDOW_STYLE)(PInvoke.TTS_ALWAYSTIP | PInvoke.TTS_NOPREFIX), 0, 0, 0, 0, owner, HMENU.Null, HiddenWindow.Instance, null);
        }

        PInvoke.SendMessage(_tips, PInvoke.TTM_SETMAXTIPWIDTH, 0, MaxWidth);
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

    public void Dispose()
    {
        if (!_tips.IsNull)
        {
            PInvoke.DestroyWindow(_tips);
        }
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
