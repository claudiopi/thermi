using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// Keeps the overlay above the taskbar, which Windows 11 lifts over every topmost window when it is used.
/// See docs/adr/0004-above-the-taskbar.md.
/// </summary>
internal sealed class Topmost : IDisposable
{
    private static readonly string[] Taskbars = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private readonly HWND _window;
#pragma warning disable S1450 // A local would be collected while Windows still calls it.
    private readonly WINEVENTPROC _procedure;
#pragma warning restore S1450
    private readonly UnhookWinEventSafeHandle _hook;
    private readonly char[] _className = new char[64];

    public Topmost(HWND window)
    {
        _window = window;
        _procedure = OnReorder;
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_REORDER, PInvoke.EVENT_OBJECT_REORDER, null, _procedure, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>Sent to every top-level window when Explorer restarts and builds a new taskbar.</summary>
    public static uint TaskbarCreated { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    /// <summary>
    /// True while one of thermi's menus shows, on the thread that shows it: no overlay rises then, since the menu is
    /// topmost too, and an overlay raised would cover it.
    /// </summary>
    [field: ThreadStatic]
    public static bool MenuShowing { get; set; }

    /// <summary>
    /// Goes back on top when a taskbar is above it, as the reorder event does, for the once-a-second safety net and a new
    /// taskbar. Only a taskbar makes it rise: rising over any other window would start a fight with every other
    /// always-on-top app. The windows it owns, its value tooltip, rise with it, so it never covers a tooltip that shows.
    /// </summary>
    public void Raise()
    {
        if (!MenuShowing && TaskbarIsAbove())
        {
            PInvoke.SetWindowPos(_window, HWND.HWND_TOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
    }

    // Disposed here, on the thread that set the hook: its handle's finalizer would unhook it from the finalizer's thread,
    // where UnhookWinEvent fails.
    public void Dispose() => _hook.Dispose();

    // Windows reports every change in the order of windows.
    private void OnReorder(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int objectId, int childId, uint thread, uint time) => Raise();

    private bool TaskbarIsAbove()
    {
        for (var window = PInvoke.GetTopWindow(HWND.Null); !window.IsNull && window != _window; window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            var length = PInvoke.GetClassName(window, _className);
            if (length > 0 && Taskbars.Contains(new string(_className, 0, length), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
