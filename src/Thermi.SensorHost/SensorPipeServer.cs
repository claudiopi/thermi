using System.Buffers;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// Writes one snapshot per second to every connected client, and reads sensors only while someone listens.
/// It never reads from the pipe: there is no client input to trust. If either loop fails, the whole server
/// fails, so the service stops and Windows restarts it instead of leaving it alive and silent.
/// </summary>
internal sealed class SensorPipeServer(Func<Snapshot> read, TimeProvider time, string pipeName = SnapshotJson.PipeName) : IDisposable
{
    public const int MaxClients = 4;

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

    // The line of each second, written straight as UTF-8: a snapshot as a string and then as bytes would be two objects
    // near the large object heap's size every second.
    private ArrayBufferWriter<byte> _line = new();

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
    // of the server's.
    private async Task<bool> AcceptOneAsync(CancellationToken stop)
    {
#pragma warning disable CA2000 // Disposed on failure here; once connected, _clients owns it and Drop or RunAsync dispose it.
        var pipe = CreatePipe();
#pragma warning restore CA2000
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

        lock (_lock)
        {
            _clients.Add(new Client(pipe));
        }

        return true;
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
            if (anySending)
            {
                _line = new ArrayBufferWriter<byte>(_line.Capacity);
            }
            else
            {
                _line.ResetWrittenCount();
            }

            SnapshotJson.WriteLine(read(), _line);
            foreach (var client in ready)
            {
                client.Sending = SendAsync(client, _line.WrittenMemory);
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

    // The first instance must be new: a pipe someone else created is not ours to join.
    private NamedPipeServerStream CreatePipe()
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
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(host.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.Read | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    private sealed class Client(NamedPipeServerStream pipe) : IDisposable
    {
        public NamedPipeServerStream Pipe { get; } = pipe;

        public Task Sending { get; set; } = Task.CompletedTask;

        public void Dispose() => Pipe.Dispose();
    }
}
