using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Ui.Web;

/// <summary>
/// The window's thread as a synchronization context: what is posted runs there, in its message loop, through a message
/// to the window, so WebView2's awaits, the clock's ticks and the sensors' snapshots all land on the thread WebView2 and
/// the window need. A message to the window, unlike one to the thread, is not lost while a move or a menu runs its own loop.
/// </summary>
internal sealed class WindowTurn : SynchronizationContext
{
    /// <summary>The window's own message that runs what was posted.</summary>
    public const uint Run = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

    public HWND Window { get; set; }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _posted.Enqueue((d, state));
        PInvoke.PostMessage(Window, Run, 0, 0);
    }

    public void Post(Action action) => Post(_ => action(), null);

    // Only the window's own thread waits on itself here, which never happens: everything it awaits is posted.
    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs, on the window's thread, everything posted so far.</summary>
    public void RunPosted()
    {
        while (_posted.TryDequeue(out var posted))
        {
            posted.Callback(posted.State);
        }
    }
}
