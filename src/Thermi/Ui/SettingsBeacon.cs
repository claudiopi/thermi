using Thermi.Core.Messages;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Ui;

/// <summary>
/// The window process's hidden top-level window, of class <c>thermi-settings</c>, which the overlay and a second
/// <c>thermi settings</c> find: "raise" brings the open window to the front on the overlay asked for, counted from 1, or
/// 0 to keep the one it shows, and the overlay's messages come here decoded.
/// </summary>
internal sealed class SettingsBeacon : IDisposable
{
    private readonly Action<int> _raised;

    // Held in a field so the garbage collector never frees it while Windows still calls it.
    private readonly WNDPROC _procedure;

    /// <param name="raised">Called on this thread with the overlay asked for; it must not throw, since Windows calls it.</param>
    public SettingsBeacon(Action<int> raised)
    {
        _raised = raised;
        _procedure = WndProc;
        HiddenWindow.Register(HiddenWindow.WindowClass, name => HiddenWindow.Plain(_procedure, name));
        Hwnd = HiddenWindow.Create(HiddenWindow.WindowClass);
    }

    public HWND Hwnd { get; }

    /// <summary>Each message from the overlay or a second thermi, decoded, on this thread.</summary>
    public Action<WindowMessage>? Received { get; init; }

    /// <summary>
    /// Asks the open window to come to the front on an overlay, counted from 1, or 0 to keep its own, with an item to
    /// select, and lets it take the front, which this process may give away while it came from a click or a terminal;
    /// false when no window answers.
    /// </summary>
    public static bool AskToRaise(int overlay, string? item = null) =>
        HiddenWindow.Send(HiddenWindow.WindowClass, new Raise(overlay, item), HiddenWindow.LetTakeTheFront);

    public void Dispose()
    {
        PInvoke.DestroyWindow(Hwnd);
        HiddenWindow.Unregister(HiddenWindow.WindowClass);
        GC.KeepAlive(_procedure);
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (HiddenWindow.Receive(message, wParam, lParam, HiddenWindow.HostClass, out var dropped) is { } received)
        {
            if (received is Raise raise)
            {
                _raised(raise.Overlay);
            }

            Received?.Invoke(received);
            return new LRESULT(1);
        }

        return dropped ? new LRESULT(0) : PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
