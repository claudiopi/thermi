using Microsoft.Extensions.Logging;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Logging;
using Thermi.Overlay;

namespace Thermi.Ui.Views;

/// <summary>
/// The window's sensors: the pipe's, but a snapshot Core failed to read stands for the last one it read, so the window
/// goes on showing that one instead of ending; the failure is logged once, and again only after a snapshot has read.
/// Used on the window's thread alone.
/// </summary>
internal sealed class ReadSensors(ISensorFeed feed, ILogger log) : ISensorFeed
{
    private Snapshot? _read;
    private Snapshot? _unread;
    private Snapshot? _reading;
    private bool _taking;
    private bool _failing;

    public event EventHandler? Received
    {
        add => feed.Received += value;
        remove => feed.Received -= value;
    }

    public Snapshot? Latest
    {
        get
        {
            if (_taking)
            {
                return _reading;
            }

            var now = feed.Latest;
            return now is not null && ReferenceEquals(now, _unread) ? _read : now;
        }
    }

    public ServiceState State => feed.State;

    public bool Taken => feed.Taken;

    public Task RunAsync(CancellationToken stop) => feed.RunAsync(stop);

    /// <summary>
    /// What <paramref name="read"/> makes of the pipe's snapshot now, which stays its <see cref="Latest"/> throughout, or
    /// null when Core fails to read it, or already failed to.
    /// </summary>
    public T? Take<T>(Func<T> read)
        where T : class
    {
        var now = feed.Latest;
        if (now is not null && ReferenceEquals(now, _unread))
        {
            return null;
        }

        (_reading, _taking) = (now, true);
        try
        {
            var taken = read();
            (_read, _unread, _failing) = (_reading, null, false);
            return taken;
        }
#pragma warning disable CA1031 // Whatever Core throws on one snapshot, the window outlives it: it is logged, and the next one is read.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _unread = _reading;
            if (!_failing)
            {
                _failing = true;
                log.SnapshotUnread(ex);
            }

            return null;
        }
        finally
        {
            (_reading, _taking) = (null, false);
        }
    }
}
