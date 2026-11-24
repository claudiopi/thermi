using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Windows.Win32;

namespace Thermi.SensorHost;

/// <summary>
/// Writes one snapshot per second to every connected client, whole on connecting and on any change of its sensors and its
/// values alone between (<see cref="SnapshotWriter"/>), and reads sensors only while someone listens. It never reads from
/// the pipe: there is no client input to trust. If either loop fails, the whole server fails, so the service stops and
/// Windows restarts it instead of leaving it alive and silent.
/// </summary>
internal sealed class SensorPipeServer(Func<Snapshot> read, TimeProvider time, string pipeName = SnapshotJson.PipeName) : IDisposable
{
    /// <summary>Clients connected at once, across sessions; past it, new ones wait for one to leave.</summary>
    public const int MaxClients = 32;

    /// <summary>
    /// Clients one Windows session may hold: any interactive user can open the pipe, so one who holds it open from their
    /// own session never takes the slots of another's overlay and window.
    /// </summary>
    public const int PerSession = 4;

    /// <summary>How long it waits to create the pipe again while someone else holds its name.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    // ERROR_NO_DATA as an HRESULT: the client closed its end.
    private const int PipeBeingClosed = unchecked((int)0x800700E8);

    // A client that stops reading is dropped after this long, and never delays the others meanwhile.
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private static readonly PipeSecurity Security = Secure();

    private readonly List<Client> _clients = [];
    private readonly Lock _lock = new();

    // Pipe instances not yet disposed, connected or waiting. Zero means the next one must be the first.
    private int _instances;

    // A slot per client: past the limit, new clients wait for one to leave.
    private readonly SemaphoreSlim _slots = new(MaxClients);

    // Each second's lines, written straight as UTF-8: a snapshot as a string and then as bytes would be two objects
    // near the large object heap's size every second.
    private readonly SnapshotWriter _lines = new();

    public async Task RunAsync(CancellationToken stop)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task[] loops = [AcceptAsync(failed.Token), BroadcastAsync(failed.Token)];
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
            lock (_lock)
            {
                _clients.ForEach(client => client.Dispose());
                _instances -= _clients.Count;
                _clients.Clear();
            }
        }
    }

    public void Dispose() => _slots.Dispose();

    private async Task AcceptAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await _slots.WaitAsync(stop);
            bool accepted;
            try
            {
                accepted = await AcceptOneAsync(stop);
            }
            catch
            {
                _slots.Release();
                throw;
            }

            if (!accepted)
            {
                _slots.Release();
            }
        }
    }

    // False for a client that left before it was taken in: Windows says so as the pipe "being closed", which is no failure
    // of the server's. False too for one past its session's share, and after a wait while someone else holds the pipe's
    // name: failing then would restart the service into the same name, again and again.
    private async Task<bool> AcceptOneAsync(CancellationToken stop)
    {
#pragma warning disable CA2000 // Disposed on failure here; once connected, _clients owns it and Drop or RunAsync dispose it.
        if (CreatePipe() is not { } pipe)
#pragma warning restore CA2000
        {
            await Task.Delay(Retry, time, stop);
            return false;
        }

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

        return TakeIn(pipe);
    }

    // A client past its session's share is hung up at once.
    private bool TakeIn(NamedPipeServerStream pipe)
    {
        var session = SensorPInvoke.GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var id) ? id : uint.MaxValue;
#pragma warning disable CA2000 // Taken in, _clients owns it, and Drop or RunAsync dispose it; refused, it is disposed below.
        var client = new Client(pipe, session);
#pragma warning restore CA2000
        bool taken;
        lock (_lock)
        {
            taken = _clients.Count(each => each.Session == session) < PerSession;
            if (taken)
            {
                _clients.Add(client);
            }
        }

        if (!taken)
        {
            client.Dispose();
            Closed();
        }

        return taken;
    }

    private async Task BroadcastAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stop))
        {
            Client[] ready;
            bool anySending;
            lock (_lock)
            {
                ready = [.. _clients.Where(client => client.Sending.IsCompleted)];
                anySending = ready.Length < _clients.Count;
            }

            if (ready.Length == 0)
            {
                continue;
            }

            // A client still writing the last line reads it from the buffer, so it is reused only once none does.
            _lines.Next(read(), anySending);
            foreach (var client in ready)
            {
                client.Sending = SendAsync(client, _lines.LineFor(client.Catalog));
                client.Catalog = _lines.Catalog;
            }
        }
    }

    private async Task SendAsync(Client client, ReadOnlyMemory<byte> line)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout, time);
        try
        {
            await client.Pipe.WriteAsync(line, timeout.Token);
            await client.Pipe.FlushAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            Drop(client);
        }
    }

    private void Drop(Client client)
    {
        bool removed;
        lock (_lock)
        {
            removed = _clients.Remove(client);
        }

        if (removed)
        {
            client.Dispose();
            Closed();
            _slots.Release();
        }
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
            return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out, MaxClients, PipeTransmissionMode.Byte, options, 0, 64 * 1024, Security);
        }
        catch (UnauthorizedAccessException) when (first)
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

    // Local interactive users may read; SYSTEM, administrators and whoever runs the host may do more; the network gets
    // nothing. Built once: whoever runs the host never changes, and each identity asked for holds a token handle.
    private static PipeSecurity Secure()
    {
        using var host = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(host.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.Read | PipeAccessRights.Synchronize,
            AccessControlType.Allow));
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
