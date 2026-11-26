using System.Runtime.InteropServices;
using Thermi.Core.Frames;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Diagnostics.ProcessSnapshotting;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Win32;

/// <summary>
/// The process of the app in front, and the monitor its window fills, from which <c>FrontGame</c> tells the game:
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

    public (int? Process, nint? Fills) Front()
    {
        var window = PInvoke.GetForegroundWindow();
        for (var depth = 0; !window.IsNull && depth < Deepest; depth++, window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (PInvoke.GetWindowThreadProcessId(window, out var process) == 0)
            {
                return (null, null);
            }

            if ((depth == 0 || Shown(window)) && !Ours(process))
            {
                return Shell.Contains(HiddenWindow.ClassOf(window)) || IsShell(process) ? (null, null) : ((int)process, Filled(window));
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The windows the process that presents, or the one that started it, as a browser, shows on screen, never thermi's
    /// nor the shell's, which started nearly every app and whose desktop fills every monitor.
    /// </summary>
    public IReadOnlyList<AppWindow> Windows(FrameRate rate)
    {
        var windows = new List<AppWindow>();
        PInvoke.EnumWindows(
            (window, _) =>
            {
                if (PInvoke.GetWindowThreadProcessId(window, out var process) != 0
                    && (process == (uint)rate.Process || process == (uint)rate.Parent)
                    && Shown(window) && !Ours(process) && !IsShell(process) && !Shell.Contains(HiddenWindow.ClassOf(window))
                    && Placed(window) is { } placed)
                {
                    windows.Add(placed);
                }

                return true;
            },
            0);
        return windows;
    }

    private static nint? Filled(HWND window) => Placed(window) is { Fills: true } placed ? placed.Monitor : null;

    // The monitor a window is on, and whether it fills it: maximized, or as large as the monitor's work area, as a game
    // full screen, borderless or in a window of the monitor's size is, wherever that window sits.
    private static unsafe AppWindow? Placed(HWND window)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return monitor.IsNull
            ? null
            : new AppWindow(
                (nint)monitor.Value,
                PInvoke.IsZoomed(window)
                    || (PInvoke.GetMonitorInfo(monitor, ref info) && PInvoke.GetWindowRect(window, out var rect)
                        && rect.right - rect.left >= info.rcWork.right - info.rcWork.left
                        && rect.bottom - rect.top >= info.rcWork.bottom - info.rcWork.top));
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

    // Asked of the one process: Windows' list of every process costs some 12 ms of CPU on a busy PC.
    internal static unsafe uint? ParentOf(uint process)
    {
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, process);
        if (handle.IsInvalid || PInvoke.PssCaptureSnapshot(handle, PSS_CAPTURE_FLAGS.PSS_CAPTURE_NONE, 0, out var snapshot) != (uint)WIN32_ERROR.NO_ERROR)
        {
            return null;
        }

        try
        {
            var info = default(PSS_PROCESS_INFORMATION);
            return PInvoke.PssQuerySnapshot(snapshot, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_PROCESS_INFORMATION, &info, (uint)sizeof(PSS_PROCESS_INFORMATION))
                == (uint)WIN32_ERROR.NO_ERROR ? info.ParentProcessId : null;
        }
        finally
        {
            _ = PInvoke.PssFreeSnapshot(PInvoke.GetCurrentProcess(), snapshot);
        }
    }
}
