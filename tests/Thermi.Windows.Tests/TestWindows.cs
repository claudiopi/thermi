using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Thermi.Core;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: AssemblyFixture(typeof(Thermi.Windows.Tests.NoInputMethods))]

namespace Thermi.Windows.Tests;

/// <summary>What the tests ask of Windows beyond what thermi itself does.</summary>
internal static class TestWindows
{
    /// <summary>DESKTOP_ALL_ACCESS, which the metadata leaves out: every right a desktop has of its own.</summary>
    public const uint AllAccess = 0x01FF;

    /// <summary>
    /// Runs <paramref name="body"/> on a thread of its own, on a desktop of its own: what it shows is hit-tested there as
    /// on the user's, never under a locked screen or another app's window, and never shows on the user's screen.
    /// </summary>
    public static void OnOwnDesktop(Action body) => OnOwnDesktop(_ => body());

    /// <summary>As <see cref="OnOwnDesktop(Action)"/>, telling <paramref name="body"/> the desktop's name, for a process it starts there.</summary>
    public static void OnOwnDesktop(Action<string> body)
    {
        var name = "thermi-test-" + Guid.NewGuid().ToString("N");
        using var desktop = TestPInvoke.CreateDesktop(name, default, AllAccess, null);
        if (desktop.IsInvalid)
        {
            throw new Win32Exception();
        }

        ExceptionDispatchInfo? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                // COM's apartment keeps a window of its own on the thread, which no thread may take to another desktop.
                TestPInvoke.CoUninitialize();
                if (!TestPInvoke.SetThreadDesktop(desktop))
                {
                    throw new Win32Exception();
                }

                TestPInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED).ThrowOnFailure();
                body(name);
            }
#pragma warning disable CA1031 // Whatever the body throws fails the test on the test's own thread.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed = ExceptionDispatchInfo.Capture(ex);
            }
        });

        // A thread with windows is a single-threaded apartment, as the window's own thread is.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failed?.Throw();
    }

    /// <summary>Windows' own window procedure, which answers every message as a window that does nothing would.</summary>
    public static WNDPROC DefaultProcedure { get; } = PInvoke.DefWindowProc;

    /// <summary>The window a click at <paramref name="point"/> would reach.</summary>
    public static HWND WindowAt(Position point) => TestPInvoke.WindowFromPoint(new Point(point.X, point.Y));

    public static bool IsWindowVisible(HWND hwnd) => TestPInvoke.IsWindowVisible(hwnd);

    /// <summary>The process's GDI and USER objects, which a leak keeps growing.</summary>
    public static (uint Gdi, uint User) Handles()
    {
        using var process = Process.GetCurrentProcess();
        return (
            TestPInvoke.GetGuiResources(process.SafeHandle, GET_GUI_RESOURCES_FLAGS.GR_GDIOBJECTS),
            TestPInvoke.GetGuiResources(process.SafeHandle, GET_GUI_RESOURCES_FLAGS.GR_USEROBJECTS));
    }

    /// <summary>A menu's entries, top to bottom: each one's id, text and whether it is checked; a separator has no text.</summary>
    public static List<(nuint Id, string Text, bool Checked)> MenuEntries(SafeHandle menu)
    {
        var entries = new List<(nuint, string, bool)>();
        Span<char> text = stackalloc char[256];
        for (var at = 0; at < TestPInvoke.GetMenuItemCount(menu); at++)
        {
            var length = TestPInvoke.GetMenuString(menu, (uint)at, text, MENU_ITEM_FLAGS.MF_BYPOSITION);
            var state = (MENU_ITEM_STATE)TestPInvoke.GetMenuState(menu, (uint)at, MENU_ITEM_FLAGS.MF_BYPOSITION);
            entries.Add((TestPInvoke.GetMenuItemID(menu, at), new string(text[..length]), state.HasFlag(MENU_ITEM_STATE.MFS_CHECKED)));
        }

        return entries;
    }
}

/// <summary>
/// A bare layered window that shows a painted bitmap as the overlay does, with the overlay's styles and its way of putting
/// the bitmap on screen, under a window class of its own so tests can run side by side.
/// </summary>
internal sealed class LayeredTestWindow : IDisposable
{
    private readonly string _className = "thermi-test-" + Guid.NewGuid().ToString("N");

    public LayeredTestWindow(Bitmap bitmap, Position position)
    {
        HiddenWindow.Register(_className, className => OverlayWindow.WindowClass(TestWindows.DefaultProcedure, className));
        Hwnd = TopLevelWindow.Create(_className, OverlayWindow.ExStyle, WINDOW_STYLE.WS_POPUP);
        OverlayWindow.Show(Hwnd, bitmap, position);
        PInvoke.SetWindowPos(
            Hwnd,
            HWND.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
    }

    public HWND Hwnd { get; }

    public void Dispose()
    {
        PInvoke.DestroyWindow(Hwnd);
        HiddenWindow.Unregister(_className);
    }
}

/// <summary>Another app holding a hotkey, on a thread of its own as another process would, until disposed.</summary>
internal sealed class HotkeyHolder : IDisposable
{
    private const int Id = 0x7ABC;

    private readonly ManualResetEventSlim _taken = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;

    private HotkeyHolder(Hotkey hotkey)
    {
        _thread = new Thread(() =>
        {
            Held = PInvoke.RegisterHotKey(HWND.Null, Id, (HOT_KEY_MODIFIERS)hotkey.Modifiers, (uint)hotkey.Key);
            _taken.Set();
            _release.Wait();
            if (Held)
            {
                PInvoke.UnregisterHotKey(HWND.Null, Id);
            }
        });
        _thread.Start();
        _taken.Wait();
    }

    public bool Held { get; private set; }

    public static HotkeyHolder Holding(Hotkey hotkey) => new(hotkey);

    public void Dispose()
    {
        _release.Set();
        _thread.Join();
        _taken.Dispose();
        _release.Dispose();
    }
}

/// <summary>
/// No input methods in the test process, from before the first test: a test's window that shows starts them, on a thread of
/// Windows' own that ends a while later and takes its USER objects with it, in the middle of the leak test's count of the
/// process's. No test needs them. A fixture, not a module initializer, which would load thermi before coverlet measures it.
/// </summary>
public sealed class NoInputMethods
{
    public NoInputMethods()
    {
        TestPInvoke.ImmDisableIME(uint.MaxValue);
    }
}

/// <summary>A hidden window of a class thermi looks for, as another process's would be, which answers every message as Windows does.</summary>
internal sealed class HiddenTestWindow : IDisposable
{
    private readonly string _className;

    public HiddenTestWindow(string className)
    {
        _className = className;
        HiddenWindow.Register(className, name => HiddenWindow.Plain(TestWindows.DefaultProcedure, name));
        Hwnd = HiddenWindow.Create(className);
    }

    public HWND Hwnd { get; }

    public void Dispose()
    {
        PInvoke.DestroyWindow(Hwnd);
        HiddenWindow.Unregister(_className);
    }
}
