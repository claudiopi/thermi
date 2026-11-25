using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// The process of the app in front, and whether its window fills its monitor, from which <c>FrontGame</c> tells the game:
/// the foreground window's, or while a window of thermi's or of a process it started is in front, the first window under
/// it that is shown. The desktop, the taskbar and any of Explorer's windows give none: Explorer starts nearly every app,
/// so frames matched by their parent would name any game behind it.
/// </summary>
internal sealed class FrontApp
{
    // Far more windows than any desktop stacks between thermi's and the app under them.
    private const int Deepest = 64;

    private static readonly string[] Shell = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "CabinetWClass"];

    // The last process asked of, which is the same one nearly every second, and whether it is a thermi.
    private uint _asked;
    private bool _ours;

    public (int? Process, bool Fills) Front()
    {
        var window = PInvoke.GetForegroundWindow();
        for (var depth = 0; !window.IsNull && depth < Deepest; depth++, window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (PInvoke.GetWindowThreadProcessId(window, out var process) == 0)
            {
                return (null, false);
            }

            if ((depth == 0 || Shown(window)) && !Ours(process))
            {
                return Shell.Contains(HiddenWindow.ClassOf(window)) || IsShell(process) ? (null, false) : ((int)process, Fills(window));
            }
        }

        return (null, false);
    }

    // Maximized, or as large as its monitor's work area, as a game full screen, borderless or in a window of the
    // monitor's size is, wherever that window sits.
    private static bool Fills(HWND window)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL);
        return PInvoke.IsZoomed(window)
            || (!monitor.IsNull && PInvoke.GetMonitorInfo(monitor, ref info) && PInvoke.GetWindowRect(window, out var rect)
                && rect.right - rect.left >= info.rcWork.right - info.rcWork.left
                && rect.bottom - rect.top >= info.rcWork.bottom - info.rcWork.top);
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

    // A thermi's, or of a process a thermi started, as the settings window's WebView2, whose windows and frames are its own.
    private bool Ours(uint process)
    {
        if (process != _asked)
        {
            (_asked, _ours) = (process, HiddenWindow.SameProgram(process) || (ParentOf(process) is { } parent && HiddenWindow.SameProgram(parent)));
        }

        return _ours;
    }

    private static unsafe uint? ParentOf(uint process)
    {
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return null;
        }

        var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        for (var more = PInvoke.Process32FirstW(snapshot, ref entry); more; more = PInvoke.Process32NextW(snapshot, ref entry))
        {
            if (entry.th32ProcessID == process)
            {
                return entry.th32ParentProcessID;
            }
        }

        return null;
    }
}
