using System.Runtime.InteropServices;

namespace Thermi;

/// <summary>
/// Keeps the overlay above the taskbar, which Windows 11 lifts over every topmost window when it is used.
/// See docs/adr/0004-above-the-taskbar.md.
/// </summary>
internal sealed class Topmost : IDisposable
{
    private const uint EventObjectReorder = 0x8004;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const uint NextWindow = 2;

    private static readonly string[] Taskbars = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private delegate void WinEventProcedure(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time);

    private readonly nint _window;
#pragma warning disable S1450 // A local would be collected while Windows still calls it.
    private readonly WinEventProcedure _procedure;
#pragma warning restore S1450
    private readonly nint _hook;
    private readonly char[] _className = new char[64];

    public Topmost(nint window)
    {
        _window = window;
        _procedure = OnReorder;
        _hook = Native.SetWinEventHook(
            EventObjectReorder, EventObjectReorder, 0, Marshal.GetFunctionPointerForDelegate(_procedure), 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    /// <summary>Sent to every top-level window when Explorer restarts and builds a new taskbar.</summary>
    public static uint TaskbarCreated { get; } = Native.RegisterWindowMessageW("TaskbarCreated");

    public void Raise() =>
        Native.SetWindowPos(_window, Native.HwndTopmost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);

    public void Dispose()
    {
        if (_hook != 0)
        {
            Native.UnhookWinEvent(_hook);
        }
    }

    // Windows reports every change in the order of windows. Only a taskbar above the overlay makes it
    // react: answering any other window would start a fight with every other always-on-top app.
    private void OnReorder(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (TaskbarIsAbove())
        {
            Raise();
        }
    }

    private bool TaskbarIsAbove()
    {
        for (var window = Native.GetTopWindow(0); window != 0 && window != _window; window = Native.GetWindow(window, NextWindow))
        {
            var length = Native.GetClassNameW(window, _className, _className.Length);
            if (length > 0 && Taskbars.Contains(new string(_className, 0, length), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
