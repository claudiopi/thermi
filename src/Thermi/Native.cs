using System.Runtime.InteropServices;

namespace Thermi;

internal static partial class Native
{
    public const nint IdcArrow = 32512;
    public const int WsPopup = unchecked((int)0x80000000);
    public const int WsExLayered = 0x00080000;
    public const int WsExTopmost = 0x00000008;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const uint WmDestroy = 0x0002;
    public const uint WmTimer = 0x0113;
    public const uint WmDisplayChange = 0x007E;
    public const uint WmLButtonDown = 0x0201;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmExitSizeMove = 0x0232;
    public const uint WmDpiChanged = 0x02E0;
    public const uint WmNcLButtonDown = 0x00A1;
    public const uint WmApp = 0x8000;
    public const int HtCaption = 2;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpNoOwnerZOrder = 0x0200;
    public const uint TpmReturnCmd = 0x0100;
    public const uint MfString = 0x0000;
    public const uint MfSeparator = 0x0800;
    public const uint MfGrayed = 0x0001;
    public const uint UlwAlpha = 0x00000002;
    public const uint MonitorDefaultToNull = 0x00000000;
    public const uint MonitorDefaultToPrimary = 0x00000001;
    public static readonly nint HwndTopmost = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public int Size;
        public int Style;
        public nint WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint IconSmall;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public int Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Size(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(in WndClassEx wndClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint LoadCursorW(nint instance, nint cursorName);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out Msg msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in Msg msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in Msg msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(nint hwnd, nint screenDc, nint destination, in Size size, nint sourceDc, in Point source, int colorKey, in BlendFunction blend, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    public static partial nint GetTopWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassNameW(nint hwnd, [Out] char[] className, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, nint callback, uint process, uint thread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(in Rect rect, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);
}
