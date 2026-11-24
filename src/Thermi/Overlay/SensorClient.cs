using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Logging;

namespace Thermi.Overlay;

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

/// <summary>
/// Reads the sensor host's pipe, <paramref name="pipeName"/>, and waits 2 s between tries while it is gone. Only a pipe whose
/// owner is <paramref name="trusted"/>, <see cref="IsTrustedOwner"/> unless a test trusts a pipe it made itself, is read.
/// </summary>
internal sealed class SensorClient(
    TimeProvider time,
    ILogger log,
    string pipeName = SnapshotJson.PipeName,
    Func<SecurityIdentifier?, bool>? trusted = null) : ISensorFeed
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly LatestSnapshot _latest = new(time);

    // The reading and the retry wait of now, which a wake from sleep cancels.
    private CancellationTokenSource? _round;

    // The last pipe reached was not the service's.
    private bool _taken;

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
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                // Host gone, restarting, not ours or unusable: retry.
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Windows woke: the pipe is read again from a new connection at once. The service ran all along, so it is not
                // lost, and the snapshot from before the sleep ages out on its own, as stale.
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

    private void Receive(Snapshot snapshot)
    {
        _latest.Set(snapshot);
        Received?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReadAsync(CancellationToken stop)
    {
        // Identification: the host may learn who connects, but never act as this user.
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await ConnectAsync(pipe, stop);
        var ours = Trusted(pipe);
        Volatile.Write(ref _taken, !ours);
        if (!ours)
        {
            throw new UnauthorizedAccessException("The sensor pipe belongs to another program, not to the thermi-sensors service.");
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

    // Any local user can create a pipe with this name while the service restarts. Only one made by the
    // service (owned by SYSTEM) or by an administrator is the real sensor host.
    private bool Trusted(NamedPipeClientStream pipe) =>
        (trusted ?? IsTrustedOwner)(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier);

    public static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
}
