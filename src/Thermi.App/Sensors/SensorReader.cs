using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.App.Sensors;

/// <summary>
/// Reads the sensor service's lines, on a connection each OS's client opens, a pipe on Windows and a socket on Linux and
/// macOS, and waits <see cref="Retry"/> between tries while it is gone. Anyone may serve a pipe or a socket of the
/// service's name while the service restarts, so only one the client trusts is read; one it does not marks the feed
/// <see cref="Taken"/>.
/// </summary>
internal abstract class SensorReader(TimeProvider time, ILogger log) : ISensorFeed
{
    /// <summary>How long a connection may take to answer, and the wait before the next try.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly LatestSnapshot _latest = new(time);

    // The reading and the retry wait of now, which a wake from sleep cancels.
    private CancellationTokenSource? _round;

    // The last connection reached was not the service's.
    private bool _taken;

    // When the last snapshot came, on the reader's thread alone; null before the first of a connection.
    private long? _heardAt;

    public event EventHandler? Received;

    public Snapshot? Latest => _latest.Current;

    public ServiceState State => _latest.State;

    public bool Taken => Volatile.Read(ref _taken) && State == ServiceState.Stopped;

    protected TimeProvider Time => time;

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
                // No host answered: a host the client starts is started when its time comes.
                Missing();
                Lost(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Host gone, restarting, not ours or unusable: retry.
                Lost(ex.Message);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The PC woke: the service is read again from a new connection at once. It ran all along, so it is not
                // lost, and the snapshot from before the sleep ages out on its own, as stale; the sleep was no pause of it.
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
    /// The PC woke from sleep, on any thread: the service is reached again now instead of at its next retry. A snapshot
    /// from before the sleep is already too old to show.
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

    /// <summary>
    /// A connection to the service, and whether the client trusts what answered; a <see cref="TimeoutException"/> when
    /// nothing answered within <see cref="Retry"/>, and an <see cref="IOException"/> when nothing serves there.
    /// </summary>
    protected abstract Task<(Stream Connection, bool Ours)> ConnectAsync(CancellationToken stop);

    /// <summary>
    /// The connection to the Unix socket at <paramref name="path"/>, Linux's or macOS's service's, and whether one of the
    /// <paramref name="trusted"/> users serves it, as the kernel tells its peer; a timeout when nothing answered within
    /// <see cref="Retry"/>, and an <see cref="IOException"/> when nothing listens there.
    /// </summary>
    protected async Task<(Stream Connection, bool Ours)> ConnectAsync(string path, IReadOnlyList<uint?> trusted, CancellationToken stop)
    {
#pragma warning disable CA2000 // The stream it answers owns it, and each way out that answers none disposes it.
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
#pragma warning restore CA2000
        try
        {
            using var timeout = new CancellationTokenSource(Retry, Time);
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), connecting.Token);
            var peer = PeerCredentials.UserOf(socket);
            return (new NetworkStream(socket, ownsSocket: true), peer is not null && trusted.Contains(peer));
        }
        catch (OperationCanceledException ex) when (!stop.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException("The sensors' socket did not answer in time.", ex);
        }
        catch (SocketException ex)
        {
            // Nothing listens there: the service is stopped or not installed.
            socket.Dispose();
            throw new IOException($"No sensor service listens on {path}: {ex.SocketErrorCode}.", ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Nothing answered within a retry.</summary>
    protected virtual void Missing()
    {
        // The service restarts on its own.
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

    // Said only for a connection that was read, so a service that stays stopped is not said again at every retry.
    private void Lost(string reason)
    {
        if (_heardAt is { } heard)
        {
            log.SensorsLost(Math.Round(time.GetElapsedTime(heard).TotalSeconds, 1), reason);
            _heardAt = null;
        }
    }

    private async Task ReadAsync(CancellationToken stop)
    {
        var (connection, ours) = await ConnectAsync(stop);
        await using (connection)
        {
            Volatile.Write(ref _taken, !ours);
            if (!ours)
            {
                throw new UnauthorizedAccessException("The sensors' connection belongs to another program, not to thermi's sensor host.");
            }

            _latest.Connected();
            var lines = new SnapshotLines();
            var bytes = new byte[64 * 1024];
            int read;
            while ((read = await connection.ReadAsync(bytes, stop)) > 0)
            {
                foreach (var snapshot in lines.Add(bytes.AsSpan(0, read)))
                {
                    Receive(snapshot);
                }
            }
        }
    }
}
