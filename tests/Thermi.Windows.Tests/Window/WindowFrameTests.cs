using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Ui.Web;
using Thermi.Windows.Win32;
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
            var window = TopLevelWindow.Create(ClassName, WINDOW_EX_STYLE.WS_EX_APPWINDOW, WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
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

    // QA r4 (R4-01): the window, above the overlays while in front, goes just below the app the user switches to, and a
    // later deactivation with no window named never lifts it over that app again.
    [Fact]
    public void TheWindowLeftForAnotherAppStaysBelowIt()
    {
        var (belowApp, topmost, stillBelow) = (false, true, false);
        TestWindows.OnOwnDesktop(() =>
        {
            WNDPROC procedure = (window, message, wParam, lParam) => PInvoke.DefWindowProc(window, message, wParam, lParam);
            HiddenWindow.Register(ClassName, name => HiddenWindow.Plain(procedure, name));
            var window = Shown();
            var app = Shown();
            try
            {
                WindowFrame.Above(window, window);
                PInvoke.SetWindowPos(app, HWND.Null, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
                WindowFrame.Above(window, app);
                belowApp = Overlay.MenuWatcher.Above(app, window);
                topmost = ((WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE)).HasFlag(WINDOW_EX_STYLE.WS_EX_TOPMOST);
                WindowFrame.Above(window, HWND.Null);
                stillBelow = Overlay.MenuWatcher.Above(app, window);
            }
            finally
            {
                PInvoke.DestroyWindow(app);
                PInvoke.DestroyWindow(window);
                HiddenWindow.Unregister(ClassName);
                GC.KeepAlive(procedure);
            }
        });

        Assert.Equal((true, false, true), (belowApp, topmost, stillBelow));
    }

    // The window opens where it closed. A window.json that is not JSON, or holds a place it cannot read, is logged and the
    // window opens at its first place, and closing it keeps its place anew, so the next one opens there.
    [Theory]
    [InlineData("none")]
    [InlineData("unreadable")]
    [InlineData("nulls")]
    public void TheWindowOpensWhereItClosedWhateverItsFileHeld(string held)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "window-place", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var log = new ListLogger();
        var normal = default(RECT);
        try
        {
            TestWindows.OnOwnDesktop(() =>
            {
                WNDPROC procedure = (window, message, wParam, lParam) => PInvoke.DefWindowProc(window, message, wParam, lParam);
                HiddenWindow.Register(ClassName, name => HiddenWindow.Plain(procedure, name));
                var screens = Desktop.Screens();
                var file = Path.Combine(folder, "window.json");
                if (held != "none")
                {
                    File.WriteAllText(file, held == "unreadable" ? "{ not json" : $"{{\"{WindowRoom.Setup(screens)}\":[null,null,null,null,false]}}");
                }

                var closed = Shown();
                var opened = TopLevelWindow.Create(ClassName, WINDOW_EX_STYLE.WS_EX_APPWINDOW, WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
                try
                {
                    new WindowPlace(folder, log).Restore(closed, new ThermiConfig(), screens);
                    PInvoke.SetWindowPos(closed, HWND.Null, 100, 100, 400, 300, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                    new WindowPlace(folder, log).Keep(closed, screens);
                    new WindowPlace(folder, log).Restore(opened, new ThermiConfig(), screens);
                    var placement = new WINDOWPLACEMENT { length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
                    PInvoke.GetWindowPlacement(opened, ref placement);
                    normal = placement.rcNormalPosition;
                }
                finally
                {
                    PInvoke.DestroyWindow(opened);
                    PInvoke.DestroyWindow(closed);
                    HiddenWindow.Unregister(ClassName);
                    GC.KeepAlive(procedure);
                }
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        Assert.Equal((100, 100, 500, 400), (normal.left, normal.top, normal.right, normal.bottom));
    }

    private static HWND Shown()
    {
        var window = TopLevelWindow.Create(ClassName, WINDOW_EX_STYLE.WS_EX_APPWINDOW, WINDOW_STYLE.WS_OVERLAPPEDWINDOW);
        const SET_WINDOW_POS_FLAGS Shows = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos(window, HWND.Null, 100, 100, 400, 300, Shows);
        return window;
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
