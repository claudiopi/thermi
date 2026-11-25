using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.SensorHost.User;

/// <summary>
/// The user's host's hidden top-level window, on a thread of its own, which Windows tells when the session ends: at a
/// sign-out or a restart Windows ends the process with no other word, and a run that ends without its Stopped mark counts
/// as a crash, two of which soon after a switch would send a good version back. The service learns it as a service does.
/// </summary>
internal static class SessionEnd
{
    private const string ClassName = "thermi-sensors-user";

    // Held for as long as the process runs, since Windows calls it through a pointer the collector cannot see.
    private static readonly WNDPROC Procedure = Answer;

    private static Action? _ending;

    /// <summary>
    /// Calls <paramref name="ending"/> once, as the session ends, before Windows ends the process, until disposed; a window
    /// that could not be made is told to <paramref name="failed"/>, since the mark would then be missing at a session's end.
    /// </summary>
    public static IDisposable Watch(Action ending, Action<string> failed)
    {
        Volatile.Write(ref _ending, ending);
        new Thread(() => Run(failed)) { IsBackground = true, Name = "session end" }.Start();
        return new Watching();
    }

    private static unsafe void Run(Action<string> failed)
    {
        var instance = SensorPInvoke.GetModuleHandle((char*)null);
        fixed (char* name = ClassName)
        {
            var type = new WNDCLASSEXW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Procedure,
                hInstance = instance,
                lpszClassName = name,
            };
            if (SensorPInvoke.RegisterClassEx(type) == 0)
            {
                failed($"The host's session window's class was not registered: error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
                return;
            }

            // Top-level, as only those hear the session end; never shown.
            var window = SensorPInvoke.CreateWindowEx(default, name, name, default, 0, 0, 0, 0, HWND.Null, HMENU.Null, instance, null);
            if (window.IsNull)
            {
                failed($"The host's session window was not made: error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}.");
                return;
            }
        }

        while (SensorPInvoke.GetMessage(out var message, HWND.Null, 0, 0).Value > 0)
        {
            SensorPInvoke.TranslateMessage(message);
            SensorPInvoke.DispatchMessage(message);
        }
    }

    private static LRESULT Answer(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == SensorPInvoke.WM_ENDSESSION && wParam.Value != 0)
        {
            Interlocked.Exchange(ref _ending, null)?.Invoke();
            return default;
        }

        return SensorPInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private sealed class Watching : IDisposable
    {
        public void Dispose() => Volatile.Write(ref _ending, null);
    }
}
