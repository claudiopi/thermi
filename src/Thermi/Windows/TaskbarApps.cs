using System.Runtime.InteropServices;
using Thermi.Core.Placement;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// How far a taskbar's buttons reach, as <see cref="TaskbarButtons"/> tells from what UI Automation finds, since Windows
/// 11 draws them all inside one window. Asking crosses into Explorer and waits on it, so the answer is read off the
/// window's thread and kept per taskbar. It is read again when Windows says a button may have come or gone, a window
/// made or destroyed, a setting changed or a new taskbar, once the taskbar has settled, and otherwise only seldom; the
/// owner hears when an answer changed. Made and disposed on a thread that runs a message loop, which gets those events.
/// </summary>
internal sealed class TaskbarApps : IDisposable
{
    // A safety net for what no event tells, as a button pinned or unpinned.
    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(30);

    // A button that comes or goes slides the others into place first.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    // A busy or hung Explorer gives up the read within this, instead of the minutes UI Automation waits by default.
    private const uint TimeoutMs = 500;

    private static readonly uint ShellHook = PInvoke.RegisterWindowMessage("SHELLHOOK");

    // The settings window's button is named after thermi's program, as Windows names any unpinned program's.
    private static readonly string OwnProgram = Path.GetFileName(Environment.ProcessPath ?? "thermi.exe");

    private static int _made;

    private readonly TimeProvider _time;
    private readonly Action<Action> _onTurn;
    private readonly Action _changed;
    private readonly Dictionary<PixelRect, (TaskbarButtons? Buttons, long At)> _read = [];
    private readonly HashSet<PixelRect> _reading = [];
    private readonly HashSet<PixelRect> _again = [];

    // One client for every read, made on the first; UI Automation's client may be used from any thread of the pool.
    private readonly Lazy<Client> _automation = new(Client.Make);

    // Each one's own class, since a class keeps the procedure of the window it was registered for.
    private readonly string _className = $"thermi-taskbar-{Interlocked.Increment(ref _made)}";
    private readonly WNDPROC _procedure;
    private readonly HWND _events;
    private readonly ITimer _settled;

    /// <param name="time">The clock the answers are kept by.</param>
    /// <param name="onTurn">Runs an action where the answers are kept: on the window's thread, or under its lock.</param>
    /// <param name="changed">Told there when a taskbar's answer changed.</param>
    public TaskbarApps(TimeProvider time, Action<Action> onTurn, Action changed)
    {
        _time = time;
        _onTurn = onTurn;
        _changed = changed;
        _settled = time.CreateTimer(_ => onTurn(ReadAll), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _procedure = WndProc;
        HiddenWindow.Register(_className, name => HiddenWindow.Plain(_procedure, name));
        _events = HiddenWindow.Create(_className);
        PInvoke.RegisterShellHookWindow(_events);
    }

    /// <summary>
    /// The buttons' extent on the taskbar that lies at <paramref name="bar"/> as last read, or null when it cannot be read
    /// or has not been yet, which starts a read.
    /// </summary>
    public PixelRect? Extent(PixelRect bar) => Known(bar)?.Extent;

    /// <summary>The buttons' extent when they begin at the taskbar's left end, Start first, as <see cref="Snaps.LeftButtons"/> tells; null otherwise or unread.</summary>
    public PixelRect? LeftButtons(PixelRect bar) => Known(bar) is { } found ? Snaps.LeftButtons(bar, found.Extent, found.Start) : null;

    public void Dispose()
    {
        _settled.Dispose();
        PInvoke.DeregisterShellHookWindow(_events);
        PInvoke.DestroyWindow(_events);
        HiddenWindow.Unregister(_className);
    }

    private TaskbarButtons? Known(PixelRect bar)
    {
        var known = _read.TryGetValue(bar, out var read);
        if (!known || _time.GetElapsedTime(read.At) >= Kept)
        {
            Start(bar);
        }

        return known ? read.Buttons : null;
    }

    // A read already under way may have looked before the change, so the taskbar is read once more after it.
    private void Start(PixelRect bar)
    {
        if (_reading.Add(bar))
        {
            _ = ReadAsync(bar);
        }
        else
        {
            _again.Add(bar);
        }
    }

    private void ReadAll()
    {
        foreach (var bar in _read.Keys.ToList())
        {
            Start(bar);
        }
    }

    private async Task ReadAsync(PixelRect bar)
    {
        var buttons = await Task.Run(() => Read(bar));
        _onTurn(() => Keep(bar, buttons));
    }

    private void Keep(PixelRect bar, TaskbarButtons? buttons)
    {
        _reading.Remove(bar);
        var same = _read.TryGetValue(bar, out var before) && before.Buttons == buttons;
        _read[bar] = (buttons, _time.GetTimestamp());
        if (_again.Remove(bar))
        {
            Start(bar);
        }

        if (!same)
        {
            _changed();
        }
    }

    private TaskbarButtons? Read(PixelRect bar)
    {
        if (Desktop.TaskbarWindows().FirstOrDefault(each => each.Bounds == bar) is not { Bar.IsNull: false } taskbar)
        {
            return null;
        }

        try
        {
            return TaskbarButtons.Of(_automation.Value.Buttons(taskbar.Bar), bar, taskbar.Tray, OwnProgram);
        }
        catch (COMException)
        {
            return null;
        }
    }

    // Windows tells every shell hook window when a window that may have a button is made or destroyed, and every
    // top-level window when a setting changes, as the taskbar's alignment, or Explorer builds a new taskbar.
    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var shell = message == ShellHook && (uint)wParam.Value is PInvoke.HSHELL_WINDOWCREATED or PInvoke.HSHELL_WINDOWDESTROYED or PInvoke.HSHELL_WINDOWREPLACED;
        if (shell || message == PInvoke.WM_SETTINGCHANGE || message == Topmost.TaskbarCreated)
        {
            _settled.Change(Settle, Timeout.InfiniteTimeSpan);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>
    /// UI Automation's client, asking only for the buttons and Start, with only the four properties the answer needs
    /// brought back in the same call, instead of a call into Explorer for each property of every element on the taskbar.
    /// </summary>
    private sealed class Client(IUIAutomation2 automation, IUIAutomationCondition buttons, IUIAutomationCacheRequest cache)
    {
        public static Client Make()
        {
            var automation = (IUIAutomation2)new CUIAutomation8();
            automation.ConnectionTimeout = TimeoutMs;
            automation.TransactionTimeout = TimeoutMs;
            var cache = automation.CreateCacheRequest();
            cache.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
            cache.AddProperty(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId);
            cache.AutomationElementMode = AutomationElementMode.AutomationElementMode_None;
            var start = automation.CreateOrCondition(automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId, "StartButton"), automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ClassNamePropertyId, "Start"));
            var buttons = automation.CreateOrCondition(automation.CreatePropertyCondition(UIA_PROPERTY_ID.UIA_ControlTypePropertyId, (int)UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId), start);
            return new Client(automation, buttons, cache);
        }

        public TaskbarElement[] Buttons(HWND bar)
        {
            var found = automation.ElementFromHandle(bar).FindAllBuildCache(TreeScope.TreeScope_Descendants, buttons, cache);
            var elements = new TaskbarElement[found.Length];
            for (var at = 0; at < elements.Length; at++)
            {
                var element = found.GetElement(at);
                var rect = element.CachedBoundingRectangle;
                var box = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
                elements[at] = new TaskbarElement(box, element.CachedControlType == UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId, Text(element.CachedClassName), Text(element.CachedAutomationId));
            }

            return elements;
        }

        // UI Automation hands each text over as a BSTR the caller frees.
        private static string Text(BSTR text)
        {
            try
            {
                return text.ToString() ?? "";
            }
            finally
            {
                PInvoke.SysFreeString(text);
            }
        }
    }
}
