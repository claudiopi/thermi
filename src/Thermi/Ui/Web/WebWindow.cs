using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Thermi.Logging;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Ui.Web;

/// <summary>
/// The settings window: a plain Win32 window in Windows' frame, filled by the page in WebView2, with no WPF in its
/// process. It opens at its first or remembered place, shows once the page has loaded, sits above thermi's overlays
/// while it is active, and on close lets WebView2 go, so no process of it stays behind.
/// </summary>
internal sealed class WebWindow : IDisposable
{
    private const string ClassName = "thermi-window";

    private readonly WindowStart _start;
    private readonly WindowTurn _turn;
    private readonly WindowPlace _place;
    private readonly WNDPROC _procedure;
    private readonly WebPage _page;
    private readonly bool _mica;
    private ExceptionDispatchInfo? _failed;
    private bool _shown;

    public WebWindow(WindowStart start, WindowTurn turn, WebPage page)
    {
        _start = start;
        _turn = turn;
        _page = page;
        _place = new WindowPlace(start.Environment.DataFolder, start.Environment.Log);
        _procedure = WndProc;
        HiddenWindow.Register(ClassName, name => Class(_procedure, name));
        Hwnd = HiddenWindow.Create(ClassName, WINDOW_EX_STYLE.WS_EX_APPWINDOW, WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
        _turn.Window = Hwnd;
        _mica = WindowFrame.Dress(Hwnd);
        _place.Restore(Hwnd, start.Settings.Config, start.Environment.Screens());
        _page.Loaded += (_, _) => Show();
    }

    public HWND Hwnd { get; }

    /// <summary>Opens the page and runs the window's messages until it closes; a failure on its way out is thrown here.</summary>
    public void Run()
    {
        _turn.Post(() => _ = OpenAsync());
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        _failed?.Throw();
    }

    /// <summary>Comes to the front, restored if it was minimized.</summary>
    public void Raise()
    {
        if (PInvoke.IsIconic(Hwnd))
        {
            PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        PInvoke.SetForegroundWindow(Hwnd);
    }

    public void Dispose()
    {
        if (PInvoke.IsWindow(Hwnd))
        {
            PInvoke.DestroyWindow(Hwnd);
        }

        HiddenWindow.Unregister(ClassName);
        GC.KeepAlive(_procedure);
    }

    private async Task OpenAsync()
    {
        try
        {
            await _page.OpenAsync(Hwnd, Path.Combine(_start.Environment.DataFolder, "WebView2"), _start.Environment.Log.IsEnabled(LogLevel.Debug), _mica);
            Fit();
        }
#pragma warning disable CA1031 // Not handled: thrown again by Run, on the window's thread, after its loop ends.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failed = ExceptionDispatchInfo.Capture(ex);
            PInvoke.DestroyWindow(Hwnd);
        }
    }

    private static unsafe WNDCLASSEXW Class(WNDPROC procedure, PCWSTR name) => HiddenWindow.Plain(procedure, name) with
    {
        hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
        // thermi.exe's own icon, thermi's mark, which the compiler embeds under the id Windows gives a program's
        // icon; Windows takes the title bar's and taskbar's small one from the same resource.
        hIcon = PInvoke.LoadIcon(HiddenWindow.Instance, PInvoke.IDI_APPLICATION),
        style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
        // Black is clear over the frame extended into the client area, so where the page is not, as the top edge and
        // the caption buttons' hole, Windows shows its backdrop and draws the buttons.
        hbrBackground = new HBRUSH((nint)PInvoke.GetStockObject(GET_STOCK_OBJECT_FLAGS.BLACK_BRUSH).Value),
    };

    private void Show()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        PInvoke.ShowWindow(Hwnd, _place.Maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        _page.Show();
        PInvoke.SetForegroundWindow(Hwnd);
        var waited = (long)Math.Round((_start.Time.GetUtcNow() - _start.Environment.Requested).TotalMilliseconds);
        _start.Environment.Log.FirstFrame(waited);
    }

    private void Fit()
    {
        if (_page.Controller is not { } controller)
        {
            return;
        }

        var bounds = WindowFrame.PageBounds(Hwnd);
        controller.Bounds = new System.Drawing.Rectangle(bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top);
        if (WindowFrame.PageWindow(Hwnd) is { IsNull: false } page)
        {
            WindowFrame.CutButtons(Hwnd, page, bounds);
        }

        var hole = WindowFrame.Hole(Hwnd, bounds);
        var scale = PInvoke.GetDpiForWindow(Hwnd) / 96.0;
        _page.Caption((hole.right - hole.left) / scale, hole.bottom / scale);
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            // DWM answers first, as for a frame extended into the client area it must: its caption buttons' hover and
            // press, which leave their hover once Windows says the pointer left them for the page.
            if (message == PInvoke.WM_NCMOUSEMOVE)
            {
                WindowFrame.TrackLeave(hwnd);
            }

            if (message != PInvoke.WM_NCHITTEST && PInvoke.DwmDefWindowProc(hwnd, message, wParam, lParam, out var answer))
            {
                return answer;
            }

            return Handle(hwnd, message, wParam, lParam) ?? PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
#pragma warning disable CA1031 // Windows calls this: a failure ends the window, and Run throws it on its thread.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failed ??= ExceptionDispatchInfo.Capture(ex);
            PInvoke.DestroyWindow(hwnd);
            return new LRESULT(0);
        }
    }

    private LRESULT? Handle(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case WindowTurn.Run:
                _turn.RunPosted();
                return new LRESULT(0);
            case PInvoke.WM_NCCALCSIZE when wParam.Value != 0:
                return WindowFrame.CalculateSize(hwnd, wParam, lParam);
            case PInvoke.WM_NCHITTEST:
                return WindowFrame.HitTest(hwnd, message, wParam, lParam);
            case PInvoke.WM_SIZE:
            case PInvoke.WM_DPICHANGED when Resized(lParam):
                Fit();
                return null;
            case PInvoke.WM_GETMINMAXINFO:
                WindowPlace.Limit(hwnd, lParam, _start.Environment.Screens());
                return new LRESULT(0);
            case PInvoke.WM_ACTIVATE:
                WindowFrame.Above(hwnd, (wParam.Value & 0xFFFF) != 0);
                return null;
            case PInvoke.WM_SETTINGCHANGE:
                WindowFrame.Follow(hwnd);
                return null;
            case PInvoke.WM_CLOSE:
                _place.Keep(hwnd, _start.Environment.Screens());
                _page.Dispose();
                PInvoke.DestroyWindow(hwnd);
                return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            default:
                return null;
        }
    }

    // Windows' suggested place for the new DPI, which a window that follows DPI takes.
    private bool Resized(LPARAM lParam)
    {
        var suggested = Marshal.PtrToStructure<RECT>(lParam);
        PInvoke.SetWindowPos(Hwnd, HWND.Null, suggested.left, suggested.top, suggested.right - suggested.left, suggested.bottom - suggested.top, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        return true;
    }
}
