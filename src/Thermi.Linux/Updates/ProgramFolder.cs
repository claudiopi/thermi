using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Updates;

/// <summary>
/// The folder the running program came from, as the system's updater changes it: each file a package writes, renames over
/// another or removes there, the libraries the process loads later among them, is heard at once through Gio's watch of
/// the folder, and when the last one was is kept on the injected clock. Watched on the loop that made it.
/// </summary>
internal sealed class ProgramFolder : IDisposable
{
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Gio.FileMonitor? _monitor;
    private long? _changed;

    public ProgramFolder(TimeProvider time, ILogger log)
    {
        (_time, _log) = (time, log);
        try
        {
            _monitor = Gio.FileHelper.NewForPath(AppContext.BaseDirectory).MonitorDirectory(Gio.FileMonitorFlags.WatchMoves, null);
            _monitor.OnChanged += (_, _) => _changed = _time.GetTimestamp();
        }
        catch (GLib.GException)
        {
            // A folder Gio cannot watch is never seen to change: the process keeps its version until it ends.
            _monitor = null;
        }
    }

    /// <summary>How long ago a file of the program's folder last changed, or null when none did since the process started.</summary>
    public TimeSpan? SinceChange => _changed is { } at ? _time.GetElapsedTime(at) : null;

    public void Dispose()
    {
        if (_monitor is not null)
        {
            _monitor.Cancel();
            MainLoopRelease.Hold(_log, _monitor);
            _monitor.Dispose();
        }
    }
}
