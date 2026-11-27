using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Thermi.Core.Pipe;

/// <summary>
/// Who is at the other end of a Linux socket, as the kernel says with <c>SO_PEERCRED</c>: the user that opened it, which
/// neither end can fake. The service counts its clients by it, and a client trusts a server only by it.
/// </summary>
public static class PeerCredentials
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    /// <summary>The user id of the socket's peer, or null where the OS tells none.</summary>
    public static uint? UserOf(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        // struct ucred: the peer's process, user and group ids, as 32-bit numbers.
        Span<byte> credentials = stackalloc byte[12];
        try
        {
            return socket.GetRawSocketOption(SolSocket, SoPeerCred, credentials) == credentials.Length
                ? MemoryMarshal.Read<uint>(credentials[4..])
                : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
