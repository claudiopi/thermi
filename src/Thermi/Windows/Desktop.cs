using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// What the snap math, the look and the visibility need from Windows: the monitors, their taskbars and notification areas,
/// the taskbar's mode, high contrast, the window in front, what Windows says the user is doing, and the cursor.
/// </summary>
internal static class Desktop
{
    private const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string MainTaskbar = "Shell_TrayWnd";
    private const string OtherTaskbar = "Shell_SecondaryTrayWnd";

    // Made once and held for the process's life: a delegate and its native thunk per paint would be garbage every second.
    private static readonly unsafe MONITORENUMPROC CollectProcedure = Collect;

    // A monitor's id by its device instance: its EDID is read once per monitor plugged, not at every paint.
    private static readonly ConcurrentDictionary<string, string> Ids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The system's accent color, Windows' default blue when it has none to give.</summary>
    public static Rgb Accent() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) is int abgr
            ? new Rgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16))
            : new Rgb(0x00, 0x78, 0xD4);

    /// <summary>The mode of the taskbar, which Windows keeps apart from the apps' mode.</summary>
    public static bool LightTaskbar() => Registry.GetValue(Personalize, "SystemUsesLightTheme", 0) is 1;

    /// <summary>Windows' high contrast colors while high contrast is on, which replace every color of the overlay; null while it is off.</summary>
    public static unsafe HighContrastColors? HighContrast()
    {
        var info = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETHIGHCONTRAST, info.cbSize, &info, default)
            && info.dwFlags.HasFlag(HIGHCONTRASTW_FLAGS.HCF_HIGHCONTRASTON)
            ? new HighContrastColors(
                SystemColor(SYS_COLOR_INDEX.COLOR_WINDOW),
                SystemColor(SYS_COLOR_INDEX.COLOR_WINDOWTEXT),
                SystemColor(SYS_COLOR_INDEX.COLOR_HIGHLIGHT),
                SystemColor(SYS_COLOR_INDEX.COLOR_HOTLIGHT))
            : null;
    }

    /// <summary>Whether Windows' animation effects are on, without which nothing on the overlay moves by itself.</summary>
    public static unsafe bool Animations()
    {
        BOOL on = true;
        return !PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &on, default) || on;
    }

    /// <summary>The window in front, with its monitor's bounds when Windows can tell them, for the full-screen rule; null when there is none.</summary>
    public static ForegroundWindow? Foreground()
    {
        var window = PInvoke.GetForegroundWindow();
        if (window.IsNull || !PInvoke.GetWindowRect(window, out var rect))
        {
            return null;
        }

        Span<char> name = stackalloc char[256];
        var length = PInvoke.GetClassName(window, name);
        return new ForegroundWindow(new string(name[..Math.Max(0, length)]), Pixels(rect), PInvoke.IsZoomed(window), PInvoke.IsIconic(window))
        {
            Monitor = MonitorInfo(PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL)) is { } monitor
                ? Pixels(monitor.monitorInfo.rcMonitor)
                : null,
        };
    }

    /// <summary>
    /// What Windows says the user is doing: an exclusive full-screen Direct3D app, or presentation mode, which its own
    /// notifications stay quiet for; anything else, or no answer, is <see cref="NotificationState.Normal"/>.
    /// </summary>
    public static NotificationState UserState()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Failed)
        {
            return NotificationState.Normal;
        }

        return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ? NotificationState.Direct3DFullScreen
            : state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ? NotificationState.Presentation
            : NotificationState.Normal;
    }

    /// <summary>Where the cursor is, in the screen's pixels.</summary>
    public static Position Cursor()
    {
        PInvoke.GetCursorPos(out var cursor);
        return new Position(cursor.X, cursor.Y);
    }

    /// <summary>How far the cursor may move at a DPI before a press is a drag, as the system sets it.</summary>
    public static DragDistance DragDistanceAt(uint dpi) =>
        new(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXDRAG, dpi), PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYDRAG, dpi));

    /// <summary>Every monitor, with its scale, taskbar and notification area as they are now.</summary>
    public static IReadOnlyList<Screen> Screens() => OnTaskbars(Monitors(), out _);

    /// <summary>
    /// The monitors as Windows lists them, which change only with a display change or a setting's, such as the work area:
    /// their scale, taskbars and notification areas are <see cref="OnTaskbars"/>'s.
    /// </summary>
    public static IReadOnlyList<ListedMonitor> Monitors()
    {
        // One callback serves every call, so each call hands it its own list.
        var monitors = new List<HMONITOR>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            PInvoke.EnumDisplayMonitors(HDC.Null, null, CollectProcedure, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var screens = monitors.Select(Read).OfType<(ListedMonitor Monitor, string Connection)>().ToList();
        var ids = MonitorIds.Unique([.. screens.Select(each => (each.Monitor.Screen.Id, each.Connection))]);
        return [.. screens.Select((each, index) => each.Monitor with { Screen = each.Monitor.Screen with { Id = ids[index] } })];
    }

    /// <summary>
    /// The monitors with their scale, work area and taskbars read now, since a notification area grows with its icons and
    /// its clock with no message to say so, and a taskbar moved to another edge or set to hide changes the work area with
    /// only an Explorer restart to say so. <paramref name="stale"/> is true when a monitor is gone or a taskbar sits on one
    /// they do not hold.
    /// </summary>
    public static IReadOnlyList<Screen> OnTaskbars(IReadOnlyList<ListedMonitor> monitors, out bool stale)
    {
        var taskbars = Taskbars();
        stale = taskbars.Exists(bar => !monitors.Any(monitor => monitor.Handle == bar.Monitor));
        var screens = new List<Screen>(monitors.Count);
        foreach (var monitor in monitors)
        {
            var info = MonitorInfo(monitor.Handle);
            var unscaled = PInvoke.GetDpiForMonitor(monitor.Handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Failed;
            var gone = info is null || unscaled;
            stale |= gone;
            var taskbar = taskbars.FirstOrDefault(each => each.Monitor == monitor.Handle);
            var work = info is { } now ? Pixels(now.monitorInfo.rcWork) : monitor.Screen.Work;
            screens.Add(monitor.Screen with
            {
                Scale = (gone ? 96 : dpi) / 96.0,
                Work = work,
                Taskbar = taskbar.Monitor.IsNull ? null : taskbar.Bar,
                Tray = taskbar.Tray,
            });
        }

        return screens;
    }

    private static unsafe BOOL Collect(HMONITOR monitor, HDC dc, RECT* clip, LPARAM data)
    {
        ((List<HMONITOR>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return true;
    }

    // MONITORINFOEXW begins with MONITORINFO, and its size tells Windows to fill the device name too.
    private static MONITORINFOEXW? MonitorInfo(HMONITOR monitor)
    {
        var info = new MONITORINFOEXW { monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() } };
        return !monitor.IsNull && PInvoke.GetMonitorInfo(monitor, ref Unsafe.As<MONITORINFOEXW, MONITORINFO>(ref info)) ? info : null;
    }

    // One unplugged since it was listed has no info, and read anyway it would place the overlay at zero.
    private static (ListedMonitor, string)? Read(HMONITOR monitor)
    {
        if (MonitorInfo(monitor) is not { } info)
        {
            return null;
        }

        var (id, connection) = Identify(info.szDevice.ToString());
        var screen = new Screen(
            id,
            (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0,
            Pixels(info.monitorInfo.rcMonitor),
            Pixels(info.monitorInfo.rcWork),
            1);
        return (new ListedMonitor(monitor, screen), connection);
    }

    // The monitor shown through a GDI device name, such as \\.\DISPLAY2, by its device interface, as
    // \\?\DISPLAY#DEL40F8#{instance}#{class}, the instance ending in the port's UID: that third part is the connection,
    // and the first three name the registry key that holds its EDID. Without one, as on a remote session, the device name
    // is all there is.
    private static (string Id, string Connection) Identify(string device)
    {
        var display = new DISPLAY_DEVICEW { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>() };
        for (uint index = 0; PInvoke.EnumDisplayDevices(device, index, ref display, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME); index++)
        {
            var parts = display.DeviceID.ToString().Split('#');
            if (display.StateFlags.HasFlag(DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ACTIVE) && parts.Length == 4)
            {
                var path = string.Join('\\', parts[0].Replace(@"\\?\", "", StringComparison.Ordinal), parts[1], parts[2]);
                return (Ids.GetOrAdd(path, key => MonitorIds.Of(Edid(key), parts[2])), parts[2]);
            }
        }

        return (device, device);
    }

    // Plain users may read a monitor's EDID where Windows keeps it.
    private static byte[] Edid(string instance)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instance}\Device Parameters");
            return key?.GetValue("EDID") as byte[] ?? [];
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Every taskbar's window, with where its notification area is: the main taskbar holds it, another shows a clock,
    /// each in a window of its own, under the same classes on Windows 10 and 11.
    /// </summary>
    public static IEnumerable<(HWND Bar, PixelRect Bounds, PixelRect? Tray)> TaskbarWindows()
    {
        foreach (var (taskbarClass, trayClass) in new[] { (MainTaskbar, "TrayNotifyWnd"), (OtherTaskbar, "ClockButton") })
        {
            for (
                var bar = PInvoke.FindWindowEx(HWND.Null, HWND.Null, taskbarClass, null);
                !bar.IsNull;
                bar = PInvoke.FindWindowEx(HWND.Null, bar, taskbarClass, null))
            {
                PInvoke.GetWindowRect(bar, out var rect);
                var tray = PInvoke.FindWindowEx(bar, HWND.Null, trayClass, null);
                yield return (bar, Pixels(rect), !tray.IsNull && PInvoke.GetWindowRect(tray, out var trayBounds) ? Pixels(trayBounds) : null);
            }
        }
    }

    private static List<(HMONITOR Monitor, PixelRect Bar, PixelRect? Tray)> Taskbars() =>
        [.. TaskbarWindows().Select(each =>
            (PInvoke.MonitorFromRect(Rect(each.Bounds), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST), each.Bounds, each.Tray))];

    private static RECT Rect(PixelRect pixels) => new() { left = pixels.Left, top = pixels.Top, right = pixels.Right, bottom = pixels.Bottom };

    // Windows gives a color as 0x00BBGGRR.
    private static Rgb SystemColor(SYS_COLOR_INDEX index)
    {
        var color = PInvoke.GetSysColor(index);
        return new Rgb((byte)color, (byte)(color >> 8), (byte)(color >> 16));
    }

    private static PixelRect Pixels(RECT rect) => new(rect.left, rect.top, rect.right, rect.bottom);
}

/// <summary>A monitor as Windows listed it, by its handle, before its scale and taskbar are read.</summary>
internal readonly record struct ListedMonitor(HMONITOR Handle, Screen Screen);
