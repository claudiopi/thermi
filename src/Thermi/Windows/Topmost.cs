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
    private const string Taskbar = "Shell_TrayWnd";
    private const string SecondaryTaskbar = "Shell_SecondaryTrayWnd";

    private readonly HWND _window;
    // Past the taskbars' names, so a longer one that starts as theirs is never cut down to it.
    private readonly char[] _className = new char[64];

    public Topmost(HWND window)
    {
        _window = window;
        Reorders.Add(this);
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
            const SET_WINDOW_POS_FLAGS Flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
            PInvoke.SetWindowPos(_window, HWND.HWND_TOPMOST, 0, 0, 0, 0, Flags);
        }
    }

    public void Dispose() => Reorders.Remove(this);

    // Compared in the buffer it is read into: no string for each window on the way.
    private bool TaskbarIsAbove()
    {
        for (var window = PInvoke.GetTopWindow(HWND.Null); !window.IsNull && window != _window; window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            var name = _className.AsSpan(0, PInvoke.GetClassName(window, _className));
            if (name.SequenceEqual(Taskbar) || name.SequenceEqual(SecondaryTaskbar))
            {
                return true;
            }
        }

        return false;
    }

    // Windows reports every change in the order of windows, to every hook: the overlays of a thread share one, which
    // Windows calls on that thread, so a host with several overlays is called once per change.
    private static class Reorders
    {
        [ThreadStatic]
        private static List<Topmost>? _kept;

        [ThreadStatic]
        private static UnhookWinEventSafeHandle? _hook;

        // Kept while the hook is: a delegate only Windows holds would be collected while Windows still calls it.
        [ThreadStatic]
        private static WINEVENTPROC? _procedure;

        public static void Add(Topmost topmost)
        {
            _kept ??= [];
            _kept.Add(topmost);
            if (_hook is null)
            {
                _procedure = OnReorder;
                _hook = PInvoke.SetWinEventHook(
                    PInvoke.EVENT_OBJECT_REORDER,
                    PInvoke.EVENT_OBJECT_REORDER,
                    null,
                    _procedure,
                    0,
                    0,
                    PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
            }
        }

        // Unhooked here, on the thread that set the hook, by the last of its overlays: its handle's finalizer would unhook
        // it from the finalizer's thread, where UnhookWinEvent fails.
        public static void Remove(Topmost topmost)
        {
            if (_kept?.Remove(topmost) == true && _kept.Count == 0)
            {
                _hook?.Dispose();
                (_hook, _procedure) = (null, null);
            }
        }

        private static void OnReorder(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int objectId, int childId, uint thread, uint time)
        {
            // A copy: raising one may run another's message, which can close it.
            foreach (var topmost in _kept?.ToArray() ?? [])
            {
                topmost.Raise();
            }
        }
    }
}
