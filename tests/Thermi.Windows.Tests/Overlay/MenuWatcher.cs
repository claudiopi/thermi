using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// Looks at a popup menu while its own loop runs, from a window of the test's on the menu's thread: <c>look</c> runs
/// once the menu shows, as often as it answers false, and the menu closes once it answers true, or after enough tries.
/// </summary>
internal sealed class MenuWatcher : IDisposable
{
    private const uint Look = PInvoke.WM_APP + 0x3F0;
    private const int MaxTries = 2000;
    private const string MenuClass = "#32768";

    private readonly string _className = "thermi-test-" + Guid.NewGuid().ToString("N");
    private readonly WNDPROC _procedure;
    private readonly HWND _window;
    private int _tries;

    public MenuWatcher(Func<bool> look)
    {
        _procedure = (window, message, wParam, lParam) =>
        {
            if (message != Look)
            {
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
            }

            if (look() || ++_tries >= MaxTries)
            {
                TestPInvoke.EndMenu();
            }
            else
            {
                PInvoke.PostMessage(window, Look, 0, 0);
            }

            return new LRESULT(0);
        };
        HiddenWindow.Register(_className, name => HiddenWindow.Plain(_procedure, name));
        _window = HiddenWindow.Create(_className);
        PInvoke.PostMessage(_window, Look, 0, 0);
    }

    /// <summary>The popup menu that shows now, or null.</summary>
    public static HWND? OpenMenu()
    {
        var menu = PInvoke.FindWindowEx(HWND.Null, HWND.Null, MenuClass, null);
        return !menu.IsNull && TestPInvoke.IsWindowVisible(menu) ? menu : null;
    }

    /// <summary>Whether <paramref name="window"/> is above <paramref name="other"/> on the screen.</summary>
    public static bool Above(HWND window, HWND other)
    {
        for (var at = PInvoke.GetTopWindow(HWND.Null); !at.IsNull; at = PInvoke.GetWindow(at, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (at == window || at == other)
            {
                return at == window;
            }
        }

        return false;
    }

    public void Dispose()
    {
        PInvoke.DestroyWindow(_window);
        HiddenWindow.Unregister(_className);
        GC.KeepAlive(_procedure);
    }
}
