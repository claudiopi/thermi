using Thermi.Core.Sensors;

namespace Thermi.Core.Pipe;

/// <summary>
/// What the service of every OS does with the clients it took in: writes one snapshot per second to each, whole on
/// connecting and on any change of its sensors and its values alone between (<see cref="SnapshotWriter"/>), and reads
/// sensors only while someone listens. It never reads from a client: there is no client input to trust. Each OS's server
/// accepts its clients, a pipe's on Windows and a socket's on Linux, and hands each in with who it belongs to.
/// </summary>
public sealed class SnapshotBroadcast(Func<CancellationToken, Task<Snapshot>> read, TimeProvider time) : IDisposable
{
    /// <summary>Clients connected at once, across users; past it, new ones wait for one to leave.</summary>
    public const int MaxClients = 32;

    /// <summary>
    /// Clients one owner may hold, a Windows session or a Linux user: anyone may connect, so one who holds connections open
    /// never takes the slots of another's overlay and window.
    /// </summary>
    public const int PerOwner = 8;

    /// <summary>
    /// How long a service that serves while someone listens, Linux's or Windows' of one user, waits after its last client
    /// left: long enough for the overlay or the window to come back from a restart.
    /// </summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);

    /// <summary>A client that stops reading is dropped after this long, and never delays the others meanwhile.</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long one read of the sensors may take: a drive's SMART or a rescan of the hardware takes seconds, never this.</summary>
    public static readonly TimeSpan ReadLimit = TimeSpan.FromSeconds(60);

    private readonly List<Client> _clients = [];
    private readonly Lock _lock = new();

    private readonly SemaphoreSlim _slots = new(MaxClients);
    private long? _emptySince;

    // Each second's lines, written straight as UTF-8: a snapshot as a string and then as bytes would be two objects
    // near the large object heap's size every second.
    private readonly SnapshotWriter _lines = new();

    /// <summary>
    /// What ends a read past <see cref="ReadLimit"/>: the process, which the service manager restarts, since the stuck thread
    /// holds the hardware and a stop could wait on it forever. Tests end it their own way.
    /// </summary>
    public Action Hung { get; set; } = End;

    /// <summary>Clients connected now; one that left counts until the next second's write to it fails.</summary>
    public int Clients
    {
        get
        {
            lock (_lock)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>
    /// The clients connected now, and when the last one left, as the last write to it failed, null while any is connected
    /// or before the first; read together, as a client that comes between two reads would be seen as gone.
    /// </summary>
    public (int Clients, long? EmptySince) Seats
    {
        get
        {
            lock (_lock)
            {
                return (_clients.Count, _emptySince);
            }
        }
    }

    public static void End() =>
#pragma warning disable RS0030 // Only a process that ends frees a thread stuck in a driver, so the service manager restarts it.
        Environment.FailFast("A sensor read did not return within its limit.");
#pragma warning restore RS0030

    /// <summary>Waits for a free slot, which the client taken in next holds, or <see cref="Release"/> gives back.</summary>
    public Task SlotAsync(CancellationToken stop) => _slots.WaitAsync(stop);

    /// <summary>Gives back a slot no client was taken into.</summary>
    public void Release() => _slots.Release();

    /// <summary>
    /// Takes in a client on the slot it waited for, unless its owner holds <see cref="PerOwner"/> already: then it is closed
    /// and false returned, and the caller gives the slot back. <paramref name="closed"/> runs once its stream is disposed,
    /// either way.
    /// </summary>
    public bool TakeIn(Stream stream, uint owner, Action? closed = null)
    {
#pragma warning disable CA2000 // Taken in, _clients owns it, and Drop or Close dispose it; refused, it is disposed below.
        var client = new Client(stream, owner, closed);
#pragma warning restore CA2000
        bool taken;
        lock (_lock)
        {
            taken = _clients.Count(each => each.Owner == owner) < PerOwner;
            if (taken)
            {
                _clients.Add(client);
                _emptySince = null;
            }
        }

        if (!taken)
        {
            client.Dispose();
        }

        return taken;
    }

    /// <summary>Writes each second to the clients taken in, until <paramref name="stop"/>; a read that fails fails it.</summary>
    public async Task RunAsync(CancellationToken stop)
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
            _lines.Next(await ReadAsync(stop), anySending);
            foreach (var client in ready)
            {
                client.Sending = SendAsync(client, _lines.LineFor(client.Catalog));
                client.Catalog = _lines.Catalog;
            }
        }
    }

    public void Close()
    {
        Client[] clients;
        lock (_lock)
        {
            clients = [.. _clients];
            _clients.Clear();
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    public void Dispose() => _slots.Dispose();

    // On a thread of its own, so a read that never returns is seen.
    private async Task<Snapshot> ReadAsync(CancellationToken stop)
    {
        try
        {
            return await Task.Run(() => read(stop), CancellationToken.None).WaitAsync(ReadLimit, time, stop);
        }
        catch (TimeoutException)
        {
            Hung();
            throw;
        }
    }

    private async Task SendAsync(Client client, ReadOnlyMemory<byte> line)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout, time);
        try
        {
            await client.Stream.WriteAsync(line, timeout.Token);
            await client.Stream.FlushAsync(timeout.Token);
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
            if (removed && _clients.Count == 0)
            {
                _emptySince = time.GetTimestamp();
            }
        }

        if (removed)
        {
            client.Dispose();
            _slots.Release();
        }
    }

    private sealed class Client(Stream stream, uint owner, Action? closed) : IDisposable
    {
        public Stream Stream { get; } = stream;

        public uint Owner { get; } = owner;

        public Task Sending { get; set; } = Task.CompletedTask;

        /// <summary>The catalog of the last whole snapshot it was sent, 0 before the first (<see cref="SnapshotWriter.Catalog"/>).</summary>
        public int Catalog { get; set; }

        public void Dispose()
        {
            Stream.Dispose();
            closed?.Invoke();
        }
    }
}
