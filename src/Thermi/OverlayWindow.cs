using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using Thermi.Core;

namespace Thermi;

internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "thermi-overlay";
    private const nuint TickTimer = 1;
    private const nuint ReloadTimer = 3;
    private const uint ReloadDelayMilliseconds = 300;
    private const nuint MenuProblem = 1;
    private const nuint MenuSettings = 2;
    private const nuint MenuExit = 3;
    private const uint ConfigChanged = Native.WmApp + 1;

    private delegate nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam);

    private readonly ConfigStore _store;
    private readonly Settings _settings;
    private readonly SensorClient _sensors;
    private readonly CultureInfo _culture;
    private readonly TimeProvider _time;
    private readonly Painter _painter = new();
    private readonly FileSystemWatcher? _watcher;

    // Held in a field so the garbage collector never frees it while Windows still calls it.
    private readonly WindowProcedure _procedure;
    private nint _hwnd;
    private Topmost? _topmost;

    public OverlayWindow(ConfigStore store, SensorClient sensors, CultureInfo culture, TimeProvider time)
    {
        _store = store;
        _sensors = sensors;
        _culture = culture;
        _time = time;
        _settings = new Settings(store, error => ErrorLog.Write(error, time));
        _settings.Load();
        _procedure = WndProc;
        _watcher = Watch(store.FilePath, () => Native.PostMessageW(_hwnd, ConfigChanged, 0, 0), time);
    }

    // Without a class cursor Windows leaves whatever cursor was last shown, often the busy one.
    internal static Native.WndClassEx WindowClass(nint procedure, nint instance, nint className) => new()
    {
        Size = Marshal.SizeOf<Native.WndClassEx>(),
        WndProc = procedure,
        Instance = instance,
        Cursor = Native.LoadCursorW(0, Native.IdcArrow),
        ClassName = className,
    };

    public void Run()
    {
        var instance = Native.GetModuleHandleW(null);
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            Native.RegisterClassExW(WindowClass(Marshal.GetFunctionPointerForDelegate(_procedure), instance, className));
            _hwnd = Native.CreateWindowExW(
                Native.WsExLayered | Native.WsExTopmost | Native.WsExToolWindow | Native.WsExNoActivate,
                ClassName, "thermi", Native.WsPopup, 0, 0, 0, 0, 0, 0, instance, 0);
            _topmost = new Topmost(_hwnd);
            Repaint();
            MoveTo(_settings.Config.Position ?? DefaultPosition());
            KeepOnScreen();
            Native.SetTimer(_hwnd, TickTimer, 1000, 0);
            while (Native.GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(msg);
                Native.DispatchMessageW(msg);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(className);
        }
    }

    public void Dispose()
    {
        _topmost?.Dispose();
        _watcher?.Dispose();
        _painter.Dispose();
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (hwnd != _hwnd)
        {
            return Native.DefWindowProcW(hwnd, message, wParam, lParam);
        }

        try
        {
            if (Handle(message, (nuint)wParam))
            {
                return 0;
            }
        }
#pragma warning disable CA1031 // Windows calls this from native code: an exception escaping it ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorLog.Write(ex, _time);
            return 0;
        }

        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private bool Handle(uint message, nuint wParam)
    {
        if (message == Topmost.TaskbarCreated)
        {
            KeepOnScreen();
            _topmost?.Raise();
            return true;
        }

        switch (message)
        {
            case Native.WmTimer when wParam == ReloadTimer:
                Native.KillTimer(_hwnd, ReloadTimer);
                Reload();
                return true;
            case Native.WmTimer:
                Tick();
                return true;
            case Native.WmDpiChanged:
                Repaint();
                return true;
            case Native.WmDisplayChange:
                KeepOnScreen();
                return true;
            case Native.WmLButtonDown:
                Native.ReleaseCapture();
                Native.SendMessageW(_hwnd, Native.WmNcLButtonDown, Native.HtCaption, 0);
                return true;
            case Native.WmExitSizeMove:
                SavePosition();
                return true;
            case Native.WmRButtonUp:
                ShowMenu();
                return true;
            case ConfigChanged:
                // A save raises several events, and the file may still be locked on the first one.
                Native.SetTimer(_hwnd, ReloadTimer, ReloadDelayMilliseconds, 0);
                return true;
            case Native.WmDestroy:
                Native.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private void Tick()
    {
        // While the file is unusable, keep trying: an editor may only have been holding it locked.
        if (_settings.Problem is not null)
        {
            Reload();
        }

        Repaint();
        _topmost?.Raise();
    }

    private void Reload()
    {
        WithoutWatching(_settings.Load);
        Repaint();
    }

    // Without access to its folder the overlay still runs, with the defaults, and the config problem says why.
    private static FileSystemWatcher? Watch(string file, Action changed, TimeProvider time)
    {
        var watcher = new FileSystemWatcher();
        try
        {
            watcher.Path = Path.GetDirectoryName(file)!;
            watcher.Filter = Path.GetFileName(file);
            // Editors save in different ways: in place, or by writing another file and renaming it over this one.
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            watcher.Dispose();
            ErrorLog.Write(ex, time);
            return null;
        }
    }

    // The overlay's own saves must not come back as changes to reload.
    private void WithoutWatching(Action action)
    {
        _watcher?.EnableRaisingEvents = false;
        try
        {
            action();
        }
        finally
        {
            _watcher?.EnableRaisingEvents = true;
        }
    }

    private void Repaint()
    {
        var readings = _sensors.Latest is { } snapshot
            ? SensorMap.Read(snapshot, _settings.Config.Sources)
            : new Dictionary<string, Reading>(StringComparer.Ordinal);
        var items = OverlayLayout.Build(_settings.Config, readings, _culture);
        if (_settings.Problem is not null)
        {
            items = [.. items, new OverlayItem("CONFIG", "error", null, Level.Critical, "error")];
        }

        var scale = Native.GetDpiForWindow(_hwnd) / 96f;
        using var bitmap = _painter.Paint(items, scale);
        Show(bitmap);
    }

    // The bitmap's alpha becomes the window's shape: rounded corners and all.
    private void Show(Bitmap bitmap)
    {
        nint screen = 0, memory = 0, hBitmap = 0, previous = 0;
        try
        {
            screen = Native.GetDC(0);
            memory = Native.CreateCompatibleDC(screen);
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = Native.SelectObject(memory, hBitmap);
            var blend = new Native.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(_hwnd, screen, 0, new Native.Size(bitmap.Width, bitmap.Height), memory, new Native.Point(0, 0), 0, blend, Native.UlwAlpha);
        }
        finally
        {
            Release(screen, memory, hBitmap, previous);
        }
    }

    private static void Release(nint screen, nint memory, nint hBitmap, nint previous)
    {
        if (previous != 0)
        {
            Native.SelectObject(memory, previous);
        }

        if (hBitmap != 0)
        {
            Native.DeleteObject(hBitmap);
        }

        if (memory != 0)
        {
            Native.DeleteDC(memory);
        }

        if (screen != 0)
        {
            _ = Native.ReleaseDC(0, screen);
        }
    }

    private void MoveTo(Position position) =>
        Native.SetWindowPos(_hwnd, Native.HwndTopmost, position.X, position.Y, 0, 0, Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow);

    // A monitor that was unplugged or rearranged must not take the overlay with it.
    private void KeepOnScreen()
    {
        Native.GetWindowRect(_hwnd, out var rect);
        if (Native.MonitorFromRect(rect, Native.MonitorDefaultToNull) == 0)
        {
            MoveTo(DefaultPosition());
        }
    }

    private void SavePosition()
    {
        Native.GetWindowRect(_hwnd, out var rect);
        WithoutWatching(() => _settings.Move(new Position(rect.Left, rect.Top)));
    }

    // Right end of the main monitor's taskbar, leaving room for the tray and the clock.
    private Position DefaultPosition()
    {
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        Native.GetMonitorInfoW(Native.MonitorFromPoint(new Native.Point(0, 0), Native.MonitorDefaultToPrimary), ref info);
        Native.GetWindowRect(_hwnd, out var own);
        var scale = Native.GetDpiForWindow(_hwnd) / 96f;
        var taskbar = info.Monitor.Bottom - info.Work.Bottom;
        var x = info.Work.Right - (own.Right - own.Left) - (int)(220 * scale);
        var y = taskbar > 0 ? info.Work.Bottom + ((taskbar - (own.Bottom - own.Top)) / 2) : info.Work.Bottom - (own.Bottom - own.Top) - (int)(8 * scale);
        return new Position(x, y);
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        if (_settings.Problem is not null)
        {
            var text = _settings.Problem.Length > 120 ? _settings.Problem[..120] + "…" : _settings.Problem;
            Native.AppendMenuW(menu, Native.MfString | Native.MfGrayed, MenuProblem, text);
            Native.AppendMenuW(menu, Native.MfSeparator, 0, null);
        }

        Native.AppendMenuW(menu, Native.MfString, MenuSettings, "Edit settings");
        Native.AppendMenuW(menu, Native.MfSeparator, 0, null);
        Native.AppendMenuW(menu, Native.MfString, MenuExit, "Exit");
        Native.GetCursorPos(out var cursor);
        Native.SetForegroundWindow(_hwnd);
        var choice = (nuint)Native.TrackPopupMenu(menu, Native.TpmReturnCmd, cursor.X, cursor.Y, 0, _hwnd, 0);
        Native.DestroyMenu(menu);
        if (choice == MenuSettings)
        {
            OpenSettings();
        }
        else if (choice == MenuExit)
        {
            Native.DestroyWindow(_hwnd);
        }
    }

    // Opens the config in the user's editor for .json files, or in Notepad when there is none.
    private void OpenSettings()
    {
        try
        {
            using var editor = Process.Start(new ProcessStartInfo(_store.FilePath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            using var notepad = Process.Start(Path.Combine(Environment.SystemDirectory, "notepad.exe"), $"\"{_store.FilePath}\"");
        }
    }
}
