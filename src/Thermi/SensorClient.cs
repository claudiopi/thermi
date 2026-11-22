using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core;

namespace Thermi;

internal sealed class SensorClient(TimeProvider time)
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    private readonly LatestSnapshot _latest = new(time);

    /// <summary>Null while the sensor host is unreachable or has gone quiet.</summary>
    public Snapshot? Latest => _latest.Current;

    public async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await ReadAsync(_latest.Set, stop);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                // The host is not running, restarting, not ours, or sent something unusable: try again shortly.
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Anything else unexpected must not end the reader for good: note it and reconnect.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorLog.Write(ex, time);
            }

            _latest.Clear();
            await Task.Delay(Retry, time, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static async Task ReadAsync(Action<Snapshot> received, CancellationToken stop)
    {
        // Identification: the host may learn who connects, but never act as this user.
        await using var pipe = new NamedPipeClientStream(".", SnapshotJson.PipeName, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(Retry, stop);
        TrustOrThrow(pipe);
        var lines = new SnapshotLines();
        var bytes = new byte[64 * 1024];
        int read;
        while ((read = await pipe.ReadAsync(bytes, stop)) > 0)
        {
            foreach (var snapshot in lines.Add(bytes.AsSpan(0, read)))
            {
                received(snapshot);
            }
        }
    }

    // Any local user can create a pipe with this name while the service restarts. Only one made by the
    // service (owned by SYSTEM) or by an administrator is the real sensor host.
    private static void TrustOrThrow(NamedPipeClientStream pipe)
    {
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (!IsTrustedOwner(owner))
        {
            throw new UnauthorizedAccessException($"The sensor pipe belongs to {owner?.Value ?? "nobody"}, not to the thermi-sensors service.");
        }
    }

    public static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));
}
