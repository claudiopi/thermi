using System.Runtime.InteropServices;
using Thermi.Core.Messages;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// thermi's window classes and the windows made from them, all of its own module: the hidden top-level windows that
/// another process of thermi finds by their class and sends a registered message to, as the overlay's host and the
/// window's beacon are, and the overlays. Not message-only, since those never receive <c>TaskbarCreated</c>.
/// </summary>
internal static class HiddenWindow
{
    /// <summary>The overlay process's hidden window, which the window and a second thermi find.</summary>
    public const string HostClass = "thermi-host";

    /// <summary>The window process's hidden window, which the overlay and a second <c>thermi settings</c> find.</summary>
    public const string WindowClass = "thermi-settings";

    // Registered once: a window procedure asks for them on every message it gets.
    private static readonly Dictionary<string, uint> IdOf =
        WindowMessages.Names.ToDictionary(name => name, name => PInvoke.RegisterWindowMessage(name), StringComparer.Ordinal);
    private static readonly HashSet<uint> Ids = [.. IdOf.Values];
    private static readonly uint HistoryAskedId = Id(new HistoryAsked());

    // The window answers at once, or it is hung.
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(500);

    public static HINSTANCE Instance { get; } = PInvoke.GetModuleHandle(default(PCWSTR));

    public static unsafe void Register(string name, Func<PCWSTR, WNDCLASSEXW> windowClass)
    {
        fixed (char* className = name)
        {
            PInvoke.RegisterClassEx(windowClass(className));
        }
    }

    public static unsafe void Unregister(string name)
    {
        fixed (char* className = name)
        {
            PInvoke.UnregisterClass(className, Instance);
        }
    }

    /// <summary>A window class with nothing but its procedure, for a window that is never shown.</summary>
    public static WNDCLASSEXW Plain(WNDPROC procedure, PCWSTR className) => new()
    {
        cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
        lpfnWndProc = procedure,
        hInstance = Instance,
        lpszClassName = className,
    };

    /// <summary>Makes a hidden tool window of a registered class, which never shows, on the taskbar or anywhere.</summary>
    public static HWND Create(string className) =>
        TopLevelWindow.Create(className, WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, WINDOW_STYLE.WS_POPUP);

    /// <summary>
    /// Sends <paramref name="message"/> to the window of that class and waits up to 500 ms for its answer: null when there
    /// is no such window, or it does not answer. <paramref name="found"/> gets the window first.
    /// </summary>
    public static nint? Ask(string className, uint message, nuint wParam = 0, Action<HWND>? found = null)
    {
        var window = PInvoke.FindWindowEx(HWND.Null, HWND.Null, className, null);
        if (window.IsNull)
        {
            return null;
        }

        found?.Invoke(window);
        var answered = PInvoke.SendMessageTimeout(
            window,
            message,
            wParam,
            0,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
            (uint)AnswerTimeout.TotalMilliseconds,
            out var answer);
        return answered != 0 ? (nint)answer : null;
    }

    /// <summary>
    /// Sends a message to the window of that class and waits up to 500 ms: one that carries nothing as its registered
    /// message, any other as <c>WM_COPYDATA</c> whose <c>dwData</c> is the kind's registered id, from this process's own
    /// hidden window of the other class, which the receiver checks. False when there is no such window, the payload is
    /// past the cap, or the window does not answer.
    /// </summary>
    public static unsafe bool Send(string className, WindowMessage message, Action<HWND>? found = null)
    {
        var window = PInvoke.FindWindowEx(HWND.Null, HWND.Null, className, null);
        if (window.IsNull)
        {
            return false;
        }

        found?.Invoke(window);
        var id = Id(message);
        if (message is HistoryAsked)
        {
            return PInvoke.SendMessageTimeout(
                window,
                id,
                0,
                0,
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                (uint)AnswerTimeout.TotalMilliseconds,
                out _) != 0;
        }

        var payload = WindowMessages.Encode(message);
        if (payload.Length > WindowMessages.MaxLength)
        {
            return false;
        }

        var sender = OwnWindow(className == HostClass ? WindowClass : HostClass);
        fixed (byte* data = payload)
        {
            var copy = new COPYDATASTRUCT { dwData = id, cbData = (uint)payload.Length, lpData = data };
            return PInvoke.SendMessageTimeout(
                window,
                PInvoke.WM_COPYDATA,
                (nuint)(nint)sender,
                (nint)(&copy),
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                (uint)AnswerTimeout.TotalMilliseconds,
                out _) != 0;
        }
    }

    /// <summary>
    /// The message a hidden window got, decoded: a <c>WM_COPYDATA</c> of a kind thermi sends, from this process's peer of
    /// class <paramref name="from"/>, or the registered message of a kind that carries nothing. A "raise" may come from
    /// any process, since a second thermi has no hidden window. Null for any other message; <paramref name="dropped"/>
    /// tells one of thermi's kinds whose payload failed, or that came from another window.
    /// </summary>
    public static unsafe WindowMessage? Receive(uint message, nuint wParam, nint lParam, string from, out bool dropped)
    {
        dropped = false;
        if (message == HistoryAskedId)
        {
            return new HistoryAsked();
        }

        if (message != PInvoke.WM_COPYDATA || lParam == 0)
        {
            return null;
        }

        var copy = (COPYDATASTRUCT*)lParam;
        if (!Ids.Contains((uint)copy->dwData))
        {
            return null;
        }

        var decoded = copy->cbData <= WindowMessages.MaxLength
            ? WindowMessages.Decode(new ReadOnlySpan<byte>(copy->lpData, (int)copy->cbData))
            : null;
        dropped = decoded is null
            || Id(decoded) != copy->dwData
            || (decoded is not Raise && !Peer((HWND)(nint)wParam, from));
        return dropped ? null : decoded;
    }

    /// <summary>
    /// Lets the process of <paramref name="window"/> take the front, which this one may give away while a click or a
    /// terminal gave it the right.
    /// </summary>
    public static void LetTakeTheFront(HWND window)
    {
        PInvoke.GetWindowThreadProcessId(window, out var process);
        PInvoke.AllowSetForegroundWindow(process);
    }

    private static uint Id(WindowMessage message) => IdOf[WindowMessages.NameOf(message)];

    // This process's hidden window of that class, which names the sender of a message; none in a second thermi.
    private static HWND OwnWindow(string className)
    {
        for (
            var window = PInvoke.FindWindowEx(HWND.Null, HWND.Null, className, null);
            !window.IsNull;
            window = PInvoke.FindWindowEx(HWND.Null, window, className, null))
        {
            PInvoke.GetWindowThreadProcessId(window, out var process);
            if (process == (uint)Environment.ProcessId)
            {
                return window;
            }
        }

        return HWND.Null;
    }

    // Windows cannot say who sent a message, so the sender names its hidden window. This filters stray messages, as one
    // from another build of thermi or a program that made a window of the same class; it proves nothing, since any
    // sender can name a real thermi window, so nothing a message asks may need more trust than the desktop's own.
    private static bool Peer(HWND claimed, string from) => ClassOf(claimed) == from && SameProgram(claimed);

    /// <summary>Whether <paramref name="window"/> is of a thermi like this one: this process, or another of the same program.</summary>
    internal static bool SameProgram(HWND window) => PInvoke.GetWindowThreadProcessId(window, out var id) != 0 && SameProgram(id);

    /// <summary>Whether the process is a thermi like this one: this process, or another of the same program.</summary>
    internal static bool SameProgram(uint id)
    {
        if (id == (uint)Environment.ProcessId)
        {
            return true;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        Span<char> path = stackalloc char[1024];
        var length = (uint)path.Length;
        return !process.IsInvalid
            && PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref length)
            && string.Equals(new string(path[..(int)length]), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    internal static string ClassOf(HWND window)
    {
        Span<char> name = stackalloc char[64];
        var length = window.IsNull ? 0 : PInvoke.GetClassName(window, name);
        return new string(name[..Math.Max(0, length)]);
    }
}
