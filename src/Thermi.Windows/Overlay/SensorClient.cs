using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Windows.Logging;

namespace Thermi.Windows.Overlay;

/// <summary>Where the window's snapshots come from: the sensor pipe, as the overlay reads it, or a recording in its tests.</summary>
internal interface ISensorFeed
{
    /// <summary>Raised on the reader's thread for every snapshot that comes, once it is <see cref="Latest"/>.</summary>
    event EventHandler? Received;

    /// <summary>Null while the sensor host is unreachable or has gone quiet.</summary>
    Snapshot? Latest { get; }

    /// <summary>Whether the readings are fresh or awaited, the service is not running, or its data went stale.</summary>
    ServiceState State { get; }

    /// <summary>Whether, the service stopped, a program other than it holds a pipe of its name, which the service then waits out.</summary>
    bool Taken => false;

    Task RunAsync(CancellationToken stop);
}

/// <summary>Which pipe a client reads, whether it trusts what answers, and what it does when none does.</summary>
internal interface ISensorPipe
{
    string Name { get; }

    bool Trusts(NamedPipeClientStream pipe);

    /// <summary>No pipe answered within a retry.</summary>
    void Missing();
}

/// <summary>One pipe, trusted by its owner: the service's, or a pipe a test made itself.</summary>
internal sealed class FixedPipe(string name, Func<SecurityIdentifier?, bool> trusted) : ISensorPipe
{
    public string Name => name;

    public bool Trusts(NamedPipeClientStream pipe) => trusted(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier);

    public void Missing()
    {
        // The service restarts on its own.
    }
}

/// <summary>
/// Reads the sensor host's pipe, as <paramref name="pipes"/> picks and trusts it, and waits 2 s between tries while it is
/// gone. Any local user can create a pipe of the service's name while the service restarts, so only a trusted one is read.
/// </summary>
internal sealed class SensorClient(TimeProvider time, ILogger log, ISensorPipe pipes) : ISensorFeed
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly LatestSnapshot _latest = new(time);

    // The reading and the retry wait of now, which a wake from sleep cancels.
    private CancellationTokenSource? _round;

    // The last pipe reached was not the service's.
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
                pipes.Missing();
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
                // Windows woke: the pipe is read again from a new connection at once. The service ran all along, so it is not
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
    /// Windows woke from sleep, on any thread: the pipe is reached again now instead of at its next retry. A snapshot from
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

    // Said only for a pipe that was read, so a service that stays stopped is not said again at every retry.
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
        // Identification: the host may learn who connects, but never act as this user.
        await using var pipe = new NamedPipeClientStream(".", pipes.Name, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await ConnectAsync(pipe, stop);
        var ours = pipes.Trusts(pipe);
        Volatile.Write(ref _taken, !ours);
        if (!ours)
        {
            throw new UnauthorizedAccessException("The sensor pipe belongs to another program, not to thermi's sensor host.");
        }

        _latest.Connected();
        var lines = new SnapshotLines();
        var bytes = new byte[64 * 1024];
        int read;
        while ((read = await pipe.ReadAsync(bytes, stop)) > 0)
        {
            foreach (var snapshot in lines.Add(bytes.AsSpan(0, read)))
            {
                Receive(snapshot);
            }
        }
    }

    // The pipe gets as long as a retry to answer, on the injected clock.
    private async Task ConnectAsync(NamedPipeClientStream pipe, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(Retry, time);
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        try
        {
            await pipe.ConnectAsync(Timeout.Infinite, connecting.Token);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !stop.IsCancellationRequested)
        {
            throw new TimeoutException("The sensor pipe did not answer in time.", ex);
        }
    }

    /// <summary>Whether the service's pipe is the service's: made by it, as SYSTEM, or by an administrator.</summary>
    public static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
}
