using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Thermi.App.Sensors;

namespace Thermi.Windows.Overlay;

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
/// Reads the sensor host's pipe, as <paramref name="pipes"/> picks and trusts it (<see cref="SensorReader"/>): any local
/// user can create a pipe of the service's name while the service restarts, so only a trusted one is read.
/// </summary>
internal sealed class SensorClient(TimeProvider time, ILogger log, ISensorPipe pipes) : SensorReader(time, log)
{
    /// <summary>Whether the service's pipe is the service's: made by it, as SYSTEM, or by an administrator.</summary>
    public static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid));

    // Identification: the host may learn who connects, but never act as this user.
    protected override async Task<(Stream Connection, bool Ours)> ConnectAsync(CancellationToken stop)
    {
        var pipe = new NamedPipeClientStream(".", pipes.Name, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await ConnectAsync(pipe, stop);
            return (pipe, pipes.Trusts(pipe));
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    protected override void Missing() => pipes.Missing();

    // The pipe gets as long as a retry to answer, on the injected clock.
    private async Task ConnectAsync(NamedPipeClientStream pipe, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(Retry, Time);
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
}
