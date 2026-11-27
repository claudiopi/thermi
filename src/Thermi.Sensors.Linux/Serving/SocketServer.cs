using System.Globalization;
using System.Net.Sockets;
using Thermi.Core.Pipe;

namespace Thermi.Sensors.Linux.Serving;

/// <summary>
/// The <c>thermi-sensors</c> socket: takes in each client, at most <see cref="SnapshotBroadcast.PerOwner"/> of one user by
/// its <c>SO_PEERCRED</c>, and hands it to <see cref="SnapshotBroadcast"/>, which writes it a snapshot per second and never
/// reads from it. As systemd's service, it ends <see cref="Idle"/> after its last client left, and systemd starts it again
/// on the next connection; in a terminal, it serves until stopped. If either loop fails, the whole server fails, so
/// systemd restarts it.
/// </summary>
internal sealed class SocketServer(Socket listener, SnapshotBroadcast broadcast, TimeProvider time)
{
    public static readonly TimeSpan Idle = SnapshotBroadcast.Linger;

    // The first socket systemd hands a service, after standard input, output and error.
    private const int FirstHanded = 3;

    /// <summary>The socket systemd listens on and handed this process, as its socket unit says, or null when it handed none.</summary>
    public static Socket? FromSystemd() =>
        Environment.GetEnvironmentVariable("LISTEN_PID") == Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            && Environment.GetEnvironmentVariable("LISTEN_FDS") == "1"
#pragma warning disable CA2000 // The socket owns the handle, and disposes it with itself.
            ? new Socket(new SafeSocketHandle(FirstHanded, ownsHandle: true))
#pragma warning restore CA2000
            : null;

    /// <summary>
    /// A socket of its own at <paramref name="path"/>, in a folder its user alone may open, for a run in a terminal; one an
    /// earlier run left there is taken over.
    /// </summary>
    public static Socket Listen(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Delete(path);
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(SnapshotBroadcast.MaxClients);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Serves until <paramref name="stop"/>, or with <paramref name="idles"/> until no client came for <see cref="Idle"/>.</summary>
    public async Task RunAsync(bool idles, CancellationToken stop)
    {
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(stop);
        List<Task> loops = [AcceptAsync(failed.Token), broadcast.RunAsync(failed.Token)];
        if (idles)
        {
            loops.Add(IdleAsync(failed.Token));
        }

        try
        {
            await Task.WhenAny(loops);
            await failed.CancelAsync();
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (failed.IsCancellationRequested)
        {
            // Stopping, or idle long enough, is the normal way out; a loop that failed throws its own failure instead.
        }
        finally
        {
            broadcast.Close();
        }
    }

    private async Task AcceptAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await broadcast.SlotAsync(stop);
            Socket client;
            try
            {
                client = await listener.AcceptAsync(stop);
            }
            catch
            {
                broadcast.Release();
                throw;
            }

            // A client whose user the kernel does not tell shares one owner with all such.
            var owner = PeerCredentials.UserOf(client) ?? uint.MaxValue;
#pragma warning disable CA2000 // Taken in or refused, the broadcast owns it and disposes it.
            if (!broadcast.TakeIn(new NetworkStream(client, ownsSocket: true), owner))
#pragma warning restore CA2000
            {
                broadcast.Release();
            }
        }
    }

    // Looks once a second, as the broadcast sees a client gone only when the next write to it fails; once none is left,
    // waits out the rest of Idle from the moment the last one went.
    private async Task IdleAsync(CancellationToken stop)
    {
        var started = time.GetTimestamp();
        while (true)
        {
            var left = broadcast.Clients == 0 ? Idle - time.GetElapsedTime(broadcast.EmptySince ?? started) : TimeSpan.FromSeconds(1);
            if (left <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(left, time, stop);
        }
    }
}
