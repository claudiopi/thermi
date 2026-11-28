using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Thermi.Core.Pipe;

/// <summary>
/// Who is at the other end of a Unix socket, as the kernel says, with Linux's <c>SO_PEERCRED</c> or macOS's
/// <c>LOCAL_PEERCRED</c>, which <c>getpeereid</c> reads too: the user that opened it, which neither end can fake. The
/// service counts its clients by it, and a client trusts a server only by it. It lives in Core, as each OS's service and
/// its clients are projects that share only Core.
/// </summary>
public static class PeerCredentials
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;

    // macOS's SOL_LOCAL and LOCAL_PEERCRED, and the size of the struct xucred it fills.
    private const int SolLocal = 0;
    private const int LocalPeerCred = 1;
    private const int XucredSize = 76;

    /// <summary>The user id of the socket's peer, or null where the OS tells none.</summary>
    public static uint? UserOf(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (OperatingSystem.IsMacOS())
        {
            return Mac(socket);
        }

        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

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

    // struct xucred: its version, then the peer's user id, as 32-bit numbers, then its groups.
    private static uint? Mac(Socket socket)
    {
        Span<byte> credentials = stackalloc byte[XucredSize];
        try
        {
            return socket.GetRawSocketOption(SolLocal, LocalPeerCred, credentials) >= 2 * sizeof(uint)
                ? MemoryMarshal.Read<uint>(credentials[sizeof(uint)..])
                : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
