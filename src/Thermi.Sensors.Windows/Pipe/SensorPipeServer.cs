using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Windows.Win32;

namespace Thermi.Sensors.Windows.Pipe;

/// <summary>
/// The <c>thermi-sensors</c> pipe: takes in each client, at most <see cref="PerSession"/> of one Windows session, and hands
/// it to <see cref="SnapshotBroadcast"/>, which writes it a snapshot per second. If either loop fails, the whole server fails,
/// so the service stops and Windows restarts it instead of leaving it alive and silent.
/// </summary>
internal sealed class SensorPipeServer(
    Func<CancellationToken, Task<Snapshot>> read, TimeProvider time, string pipeName = SnapshotJson.PipeName, PipeSecurity? security = null)
    : IDisposable
{
    /// <summary>Clients connected at once, across sessions; past it, new ones wait for one to leave.</summary>
    public const int MaxClients = SnapshotBroadcast.MaxClients;

    /// <summary>
    /// Clients one Windows session may hold: any interactive user can open the pipe, so one who holds it open from their
    /// own session never takes the slots of another's overlay and window.
    /// </summary>
    public const int PerSession = SnapshotBroadcast.PerOwner;

    /// <summary>How long it waits to create the pipe again while someone else holds its name.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    // ERROR_NO_DATA as an HRESULT: the client closed its end.
    private const int PipeBeingClosed = unchecked((int)0x800700E8);

    // ERROR_PIPE_BUSY.
    private const int PipeBusy = unchecked((int)0x800700E7);

    /// <summary>A client that stops reading is dropped after this long, and never delays the others meanwhile.</summary>
    public static readonly TimeSpan WriteTimeout = SnapshotBroadcast.WriteTimeout;

    /// <summary>How long one read of the sensors may take.</summary>
    public static readonly TimeSpan ReadLimit = SnapshotBroadcast.ReadLimit;

    private static readonly PipeSecurity Everyone = ForEveryone();

    private readonly PipeSecurity _security = security ?? Everyone;

    private readonly Lock _lock = new();

    // Pipe instances not yet disposed, connected or waiting. Zero means the next one must be the first.
    private int _instances;

    private readonly SnapshotBroadcast _broadcast = new(read, time);

    /// <summary>What ends a read stuck past <see cref="ReadLimit"/>: the process, which Windows' recovery restarts.</summary>
    public Action Hung
    {
        get => _broadcast.Hung;
        init => _broadcast.Hung = value;
    }

    /// <summary>
    /// What says, once each time, that someone else holds the pipe's name: silent, the service would look running and
    /// serve nothing. The service writes it to the event log.
    /// </summary>
    public Action Squatted { get; init; } = () => { };

    // Whether Squatted was said since the pipe was last created.
    private bool _squatSaid;

    /// <summary>Clients connected now; one that left counts until the next second's write to it fails.</summary>
    public int Clients => _broadcast.Clients;

    public async Task RunAsync(CancellationToken stop)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task[] loops = [AcceptAsync(failed.Token), _broadcast.RunAsync(failed.Token)];
        try
        {
            await Task.WhenAny(loops);
            await failed.CancelAsync();
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopping is the normal way out.
        }
        finally
        {
            _broadcast.Close();
        }
    }

    public void Dispose() => _broadcast.Dispose();

    private async Task AcceptAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await _broadcast.SlotAsync(stop);
            bool accepted;
            try
            {
                accepted = await AcceptOneAsync(stop);
            }
            catch
            {
                _broadcast.Release();
                throw;
            }

            if (!accepted)
            {
                _broadcast.Release();
            }
        }
    }

    // False for a client that left before it was taken in: Windows says so as the pipe "being closed", which is no failure
    // of the server's. False too for one past its session's share, and after a wait while someone else holds the pipe's
    // name: failing then would restart the service into the same name, again and again.
    private async Task<bool> AcceptOneAsync(CancellationToken stop)
    {
#pragma warning disable CA2000 // Disposed on failure here; once connected, the broadcast owns it and disposes it.
        if (CreatePipe() is not { } pipe)
#pragma warning restore CA2000
        {
            if (!_squatSaid)
            {
                _squatSaid = true;
                Squatted();
            }

            await Task.Delay(Retry, time, stop);
            return false;
        }

        _squatSaid = false;

        try
        {
            await pipe.WaitForConnectionAsync(stop);
        }
        catch (IOException ex) when (ex.HResult == PipeBeingClosed)
        {
            await pipe.DisposeAsync();
            Closed();
            return false;
        }
        catch
        {
            await pipe.DisposeAsync();
            Closed();
            throw;
        }

        // A client past its session's share is hung up at once.
        var session = SensorPInvoke.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var id) ? id : uint.MaxValue;
        return _broadcast.TakeIn(pipe, session, Closed);
    }

    private void Closed()
    {
        lock (_lock)
        {
            _instances--;
        }
    }

    // The first instance must be new: a pipe someone else created is not ours to join. Null while someone else holds it.
    private NamedPipeServerStream? CreatePipe()
    {
        bool first;
        lock (_lock)
        {
            first = _instances == 0;
            _instances++;
        }

        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        try
        {
            // A security object is not safe to read from two threads at once, as two servers in one process do in tests.
            lock (_security)
            {
                return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out, MaxClients, PipeTransmissionMode.Byte, options, 0, 64 * 1024, _security);
            }
        }
        // Held by someone else: refused, or, made with room for no more instances, all busy.
        catch (Exception ex) when (first && (ex is UnauthorizedAccessException || ex.HResult == PipeBusy))
        {
            Closed();
            return null;
        }
        catch
        {
            Closed();
            throw;
        }
    }

    /// <summary>
    /// The user's own host's pipe (docs/adr/0016-admin-on-demand.md): owned by <paramref name="user"/>, whom the overlay
    /// checks, and open to that user, SYSTEM and administrators alone; the network gets nothing.
    /// </summary>
    public static PipeSecurity ForUser(SecurityIdentifier user)
    {
        var security = Owned(user);
        security.SetOwner(user);
        return security;
    }

    // Local interactive users may read; SYSTEM, administrators and whoever runs the host may do more; the network gets
    // nothing. Built once: whoever runs the host never changes, and each identity asked for holds a token handle.
    private static PipeSecurity ForEveryone()
    {
        using var host = WindowsIdentity.GetCurrent();
        var security = Owned(host.User!);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.Read | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
        return security;
    }

    private static PipeSecurity Owned(SecurityIdentifier host)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(host, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return security;
    }

    private sealed class Client(NamedPipeServerStream pipe, uint session) : IDisposable
    {
        public NamedPipeServerStream Pipe { get; } = pipe;

        public uint Session { get; } = session;

        public Task Sending { get; set; } = Task.CompletedTask;

        /// <summary>The catalog of the last whole snapshot it was sent, 0 before the first (<see cref="SnapshotWriter.Catalog"/>).</summary>
        public int Catalog { get; set; }

        public void Dispose() => Pipe.Dispose();
    }
}
