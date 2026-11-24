using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Ui.Web;

/// <summary>
/// Windows' own frame around the page, with the page's title row in it: the caption is taken off the top so the page
/// draws the row, Mica shows through the page's transparent background, and Windows keeps drawing and answering the
/// caption buttons, their snap layouts included, through a hole cut in the page where they are. The page's
/// <c>app-region: drag</c> makes the rest of its row the caption, with the system menu and a double-click to maximize.
/// </summary>
internal static class WindowFrame
{
    /// <summary>
    /// Mica, the extended frame and the dark or light caption, as Windows' apps are now; false where Windows refused
    /// Mica, as Windows 10 does, so the page paints its own opaque background. Windows 10's corners stay square, as
    /// nothing asks it for rounded ones.
    /// </summary>
    public static unsafe bool Dress(HWND window)
    {
        var backdrop = DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
        var mica = PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, &backdrop, sizeof(DWM_SYSTEMBACKDROP_TYPE)).Succeeded;
        // The caption buttons are Windows' own drawing over the extended frame, on Windows 10 as on 11.
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        _ = PInvoke.DwmExtendFrameIntoClientArea(window, &margins);
        Follow(window);
        return mica;
    }

    /// <summary>Takes the dark or light caption of Windows' apps, again when the user changes it.</summary>
    public static unsafe void Follow(HWND window)
    {
        BOOL dark = !AppMode.LightApps();
        _ = PInvoke.DwmSetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, (uint)sizeof(BOOL));
    }

    /// <summary>
    /// The client area takes the caption's place, keeping the side and bottom borders. Maximized too it starts at the
    /// window's top, out past the screen's edge: DWM hit-tests its caption buttons only in a frame without a top border,
    /// and the page keeps off that edge on its own (<see cref="PageBounds"/>).
    /// </summary>
    public static unsafe LRESULT CalculateSize(HWND window, WPARAM wParam, LPARAM lParam)
    {
        var parameters = (NCCALCSIZE_PARAMS*)(nint)lParam;
        var top = parameters->rgrc._0.top;
        var answer = PInvoke.DefWindowProc(window, PInvoke.WM_NCCALCSIZE, wParam, lParam);
        parameters->rgrc._0.top = top;
        return answer;
    }

    /// <summary>The caption buttons first, which Windows answers itself, then the top edge to resize from; null for the page's.</summary>
    public static LRESULT? HitTest(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (PInvoke.DwmDefWindowProc(window, message, wParam, lParam, out var answer))
        {
            return answer;
        }

        var hit = PInvoke.DefWindowProc(window, message, wParam, lParam);
        if ((uint)hit.Value != PInvoke.HTCLIENT || PInvoke.IsZoomed(window))
        {
            return hit;
        }

        PInvoke.GetWindowRect(window, out var frame);
        var y = (short)((lParam.Value >> 16) & 0xFFFF);
        return y < frame.top + Border(window) ? new LRESULT((nint)PInvoke.HTTOP) : null;
    }

    /// <summary>Asks Windows to say when the pointer leaves the frame, as for the page, which DWM's buttons need to leave their hover.</summary>
    public static unsafe void TrackLeave(HWND window)
    {
        var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE | TRACKMOUSEEVENT_FLAGS.TME_NONCLIENT, hwndTrack = window };
        PInvoke.TrackMouseEvent(ref track);
    }

    /// <summary>The caption buttons' place in the client area, where the page leaves a hole.</summary>
    public static unsafe RECT Buttons(HWND window)
    {
        RECT buttons;
        return PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CAPTION_BUTTON_BOUNDS, &buttons, (uint)sizeof(RECT)).Succeeded ? buttons : default;
    }

    /// <summary>Where the page goes: the client area, under the top edge to resize from, which a maximized window has past the screen's edge.</summary>
    public static RECT PageBounds(HWND window)
    {
        PInvoke.GetClientRect(window, out var client);
        client.top = Border(window);
        return client;
    }

    /// <summary>The caption buttons' place in the page's own pixels, the page at <paramref name="bounds"/> in the client area.</summary>
    public static RECT Hole(HWND window, RECT bounds)
    {
        // Windows gives the buttons from the window's corner, which lies out past the invisible resize borders; maximized,
        // it draws and answers them that border lower, on the screen, than it says.
        var buttons = Buttons(window);
        PInvoke.GetWindowRect(window, out var frame);
        var client = new System.Drawing.Point(bounds.left, bounds.top);
        PInvoke.ClientToScreen(window, ref client);
        var (dx, dy) = (frame.left - client.X, frame.top - client.Y + (PInvoke.IsZoomed(window) ? Border(window) : 0));
        return new RECT(buttons.left + dx, buttons.top + dy, buttons.right + dx, buttons.bottom + dy);
    }

    /// <summary>Cuts the caption buttons out of the page's window, so Windows draws and answers them.</summary>
    public static void CutButtons(HWND window, HWND page, RECT bounds)
    {
        var buttons = Hole(window, bounds);
        var whole = PInvoke.CreateRectRgn(0, 0, bounds.right - bounds.left, bounds.bottom - bounds.top);
        var hole = PInvoke.CreateRectRgn(buttons.left, buttons.top, buttons.right, buttons.bottom);
        PInvoke.CombineRgn(whole, whole, hole, RGN_COMBINE_MODE.RGN_DIFF);
        PInvoke.DeleteObject(hole);

        // Windows owns the region once it is set, and frees it.
        if (PInvoke.SetWindowRgn(page, whole, true) == 0)
        {
            PInvoke.DeleteObject(whole);
        }
    }

    /// <summary>The top border's height, which the caption had held: resize border and padding, at the window's DPI.</summary>
    public static int Border(HWND window)
    {
        var dpi = PInvoke.GetDpiForWindow(window);
        return PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYFRAME, dpi) + PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, dpi);
    }

    /// <summary>Above thermi's overlays, which are topmost, while the window is active; among other windows while it is not.</summary>
    public static void Above(HWND window, bool active) =>
        PInvoke.SetWindowPos(window, active ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>The first child window, where WebView2 shows the page.</summary>
    public static HWND PageWindow(HWND window) => PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_CHILD);
}
