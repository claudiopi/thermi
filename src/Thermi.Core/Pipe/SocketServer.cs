using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Thermi.Core.Pipe;

/// <summary>
/// The <c>thermi-sensors</c> socket of Linux and macOS: takes in each client, at most <see cref="SnapshotBroadcast.PerOwner"/>
/// of one user by its peer's credentials (<see cref="PeerCredentials"/>), and hands it to <see cref="SnapshotBroadcast"/>,
/// which writes it a snapshot per second and never reads from it. As systemd's service or launchd's agent, it ends
/// <see cref="Idle"/> after its last client left, and the one that holds its socket starts it again on the next
/// connection; in a terminal, it serves until stopped. If either loop fails, the whole server fails, so it is restarted.
/// </summary>
public sealed class SocketServer(Socket listener, SnapshotBroadcast broadcast, TimeProvider time)
{
    public static readonly TimeSpan Idle = SnapshotBroadcast.Linger;

    /// <summary>
    /// A socket of its own at <paramref name="path"/>, in a folder its user alone may open, for a run in a terminal; one an
    /// earlier run left there is taken over.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
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
            var (clients, emptySince) = broadcast.Seats;
            var left = clients == 0 ? Idle - time.GetElapsedTime(emptySince ?? started) : TimeSpan.FromSeconds(1);
            if (left <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(left, time, stop);
        }
    }
}
