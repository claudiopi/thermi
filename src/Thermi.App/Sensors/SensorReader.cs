using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.App.Sensors;

/// <summary>Where the sensor host's readings are opened: Windows' pipe, Linux's socket, or a test's own.</summary>
internal interface ISensorSource
{
    /// <summary>
    /// The host's stream of snapshot lines. Throws <see cref="TimeoutException"/> when none answers within
    /// <see cref="SensorReader.Retry"/>, <see cref="SensorSourceTakenException"/> when what answers is not the host's, and
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot be reached.
    /// </summary>
    Task<Stream> OpenAsync(CancellationToken stop);

    /// <summary>No host answered within a retry.</summary>
    void Missing();
}

/// <summary>What answered where the sensor host should is another program's, which is never read.</summary>
internal sealed class SensorSourceTakenException(string message) : UnauthorizedAccessException(message);

/// <summary>
/// Reads the sensor host's snapshots from <paramref name="source"/>, and waits 2 s between tries while it is gone. A
/// connection that ends, a host that is not there and one that is not trusted are each tried again.
/// </summary>
internal class SensorReader(TimeProvider time, ILogger log, ISensorSource source) : ISensorFeed
{
    /// <summary>How long a host gets to answer, and the wait before the next try.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly LatestSnapshot _latest = new(time);

    // The reading and the retry wait of now, which a wake from sleep cancels.
    private CancellationTokenSource? _round;

    // The last host reached was not the service's.
    private bool _taken;

    // When the last snapshot came, on the reader's thread alone; null before the first of a connection.
    private long? _heardAt;

    public event EventHandler? Received;

    public Snapshot? Latest => _latest.Current;

    public ServiceState State => _latest.State;

    public bool Taken => Volatile.Read(ref _taken) && State == ServiceState.Stopped;

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var round = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Volatile.Write(ref _round, round);
            try
            {
                await ReadAsync(round.Token);
                Lost("the service closed it");
            }
            catch (TimeoutException ex)
            {
                // No host answered: the user's own is started when its time comes.
                source.Missing();
                Lost(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Host gone, restarting, not ours or unusable: retry.
                if (ex is SensorSourceTakenException)
                {
                    Volatile.Write(ref _taken, true);
                }

                Lost(ex.Message);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The PC woke: the host is read again from a new connection at once. It ran all along, so it is not lost,
                // and the snapshot from before the sleep ages out on its own, as stale; the sleep was no pause of it.
                _heardAt = null;
                continue;
            }
#pragma warning disable CA1031 // Anything else unexpected must not end the reader for good: note it and reconnect.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                log.SensorsFailed(ex);
            }

            // The wait starts before the loss shows, so whoever sees the service stopped knows the retry is 2 s away; a
            // wake ends it at once.
            var retry = Task.Delay(Retry, time, round.Token);
            _latest.Lost();
            await retry.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <summary>
    /// The PC woke from sleep, on any thread: the host is reached again now instead of at its next retry. A snapshot from
    /// before the sleep is already too old to show.
    /// </summary>
    public void Resume()
    {
        try
        {
            Volatile.Read(ref _round)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That round already ended, and the next one connects without waiting for a wake.
        }
    }

    // A pause long enough to show as stale is said once it ends, so the log tells a service that paused from one that went.
    private void Receive(Snapshot snapshot)
    {
        var now = time.GetTimestamp();
        if (_heardAt is { } heard && time.GetElapsedTime(heard, now) is var gap && gap > LatestSnapshot.MaxAge)
        {
            log.ReadingsPaused(Math.Round(gap.TotalSeconds, 1));
        }

        _heardAt = now;
        _latest.Set(snapshot);
        Received?.Invoke(this, EventArgs.Empty);
    }

    // Said only for a host that was read, so a service that stays stopped is not said again at every retry.
    private void Lost(string reason)
    {
        if (_heardAt is { } heard)
        {
            log.SensorPipeLost(Math.Round(time.GetElapsedTime(heard).TotalSeconds, 1), reason);
            _heardAt = null;
        }
    }

    private async Task ReadAsync(CancellationToken stop)
    {
        await using var stream = await source.OpenAsync(stop);
        Volatile.Write(ref _taken, false);
        _latest.Connected();
        var lines = new SnapshotLines();
        var bytes = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(bytes, stop)) > 0)
        {
            foreach (var snapshot in lines.Add(bytes.AsSpan(0, read)))
            {
                Receive(snapshot);
            }
        }
    }
}
