using Thermi.Ui.Web;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The settings window's frame, on a real window framed as it is, on a desktop of the tests' own: restored or maximized,
/// the hole the page leaves for Windows' caption buttons lies where Windows answers them, from its top row to its
/// bottom one, so the page covers none of them and a click there reaches Windows' own close button.
/// </summary>
public sealed class WindowFrameTests
{
    private const string ClassName = "thermi-frame-test";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePageLeavesTheCaptionButtonsToWindows(bool maximized)
    {
        TestWindows.OnOwnDesktop(() =>
        {
            WNDPROC procedure = (window, message, wParam, lParam) => message switch
            {
                PInvoke.WM_NCCALCSIZE when wParam.Value != 0 => WindowFrame.CalculateSize(window, wParam, lParam),
                PInvoke.WM_NCHITTEST => WindowFrame.HitTest(window, message, wParam, lParam) ?? new LRESULT((nint)PInvoke.HTCLIENT),
                _ => PInvoke.DefWindowProc(window, message, wParam, lParam),
            };
            HiddenWindow.Register(ClassName, name => HiddenWindow.Plain(procedure, name));
            var window = HiddenWindow.Create(ClassName, WINDOW_EX_STYLE.WS_EX_APPWINDOW, WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
            try
            {
                WindowFrame.Dress(window);
                PInvoke.SetWindowPos(
                    window,
                    HWND.Null,
                    100,
                    100,
                    900,
                    600,
                    SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                        | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED
                        | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
                if (maximized)
                {
                    PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_MAXIMIZE);
                }

                var (_, top, right, bottom) = HoleOnScreen(window);
                Assert.Equal((TestPInvoke.HTCLOSE, TestPInvoke.HTCLOSE), (HitAt(window, right - 4, top + 1), HitAt(window, right - 4, bottom - 2)));
            }
            finally
            {
                PInvoke.DestroyWindow(window);
                HiddenWindow.Unregister(ClassName);
                GC.KeepAlive(procedure);
            }
        });
    }

    // The hole is cut in the page's own pixels, the page sitting at its bounds in the client area.
    private static (int Left, int Top, int Right, int Bottom) HoleOnScreen(HWND window)
    {
        var bounds = WindowFrame.PageBounds(window);
        var hole = WindowFrame.Hole(window, bounds);
        var origin = new System.Drawing.Point(bounds.left, bounds.top);
        PInvoke.ClientToScreen(window, ref origin);
        return (origin.X + hole.left, origin.Y + hole.top, origin.X + hole.right, origin.Y + hole.bottom);
    }

    // What Windows asks the window when the pointer is at a point of the screen.
    private static uint HitAt(HWND window, int x, int y) =>
        (uint)PInvoke.SendMessage(window, PInvoke.WM_NCHITTEST, 0, (y << 16) | (x & 0xFFFF)).Value;
}
