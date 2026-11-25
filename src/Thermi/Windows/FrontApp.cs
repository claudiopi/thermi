using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// The process of the app in front, whose frames the FPS items show: the foreground window's, or while a window of thermi's
/// is in front, the first window under it that is shown. The desktop, the taskbar and any of Explorer's windows give none: Explorer
/// starts nearly every app, so frames matched by their parent would name any game behind it.
/// </summary>
internal sealed class FrontApp
{
    // Far more windows than any desktop stacks between thermi's and the app under them.
    private const int Deepest = 64;

    private static readonly string[] Shell = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "CabinetWClass"];

    // The last process asked of, which is the same one nearly every second, and whether it is a thermi.
    private uint _asked;
    private bool _ours;

    public int? Process()
    {
        var window = PInvoke.GetForegroundWindow();
        for (var depth = 0; !window.IsNull && depth < Deepest; depth++, window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (PInvoke.GetWindowThreadProcessId(window, out var process) == 0)
            {
                return null;
            }

            if ((depth == 0 || Shown(window)) && !Ours(window, process))
            {
                return Shell.Contains(HiddenWindow.ClassOf(window)) || IsShell(process) ? null : (int)process;
            }
        }

        return null;
    }

    // Explorer's own process, whose dialogs, flyouts and folder windows are the shell's too, whatever their class.
    private static bool IsShell(uint process) =>
        PInvoke.GetShellWindow() is { IsNull: false } shell && PInvoke.GetWindowThreadProcessId(shell, out var explorer) != 0 && explorer == process;

    // Shown on screen as an app: a window may be visible to Windows yet minimized, or cloaked, as a suspended Store app's;
    // a click-through or tool window over a game is another program's overlay, as a chat's or a launcher's, not the app.
    private static unsafe bool Shown(HWND window)
    {
        var cloaked = stackalloc uint[1];
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return PInvoke.IsWindowVisible(window)
            && !PInvoke.IsIconic(window)
            && !style.HasFlag(WINDOW_EX_STYLE.WS_EX_TRANSPARENT)
            && !style.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)
            && !style.HasFlag(WINDOW_EX_STYLE.WS_EX_NOACTIVATE)
            && (PInvoke.DwmGetWindowAttribute(window, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, cloaked, sizeof(uint)).Failed || cloaked[0] == 0);
    }

    private bool Ours(HWND window, uint process)
    {
        if (process != _asked)
        {
            (_asked, _ours) = (process, HiddenWindow.SameProgram(window));
        }

        return _ours;
    }
}
