using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Thermi.Linux.Overlay.Link;

/// <summary>
/// systemd's watchdog for the overlay's user unit, as Windows' service manager restarts a hung process: the unit's
/// <c>WatchdogSec</c> has systemd hand the overlay a socket and a time, and the overlay's main loop says it runs on it each
/// second, so an overlay whose loop hangs, painting nothing and answering no one, is restarted. Started by hand, with no
/// unit, it says nothing.
/// </summary>
internal sealed partial class Watchdog : IDisposable
{
    private static readonly byte[] Alive = Encoding.ASCII.GetBytes("WATCHDOG=1");

    private readonly ILogger _log;
    private readonly Socket? _socket;
    private readonly UnixDomainSocketEndPoint? _systemd;
    private bool _failed;

    public Watchdog(ILogger log)
    {
        _log = log;
        // An abstract socket's name starts with "@", which the kernel's address writes as a NUL.
        if (Environment.GetEnvironmentVariable("NOTIFY_SOCKET") is not { Length: > 0 } path || !Watched())
        {
            return;
        }

        _systemd = new UnixDomainSocketEndPoint(path[0] == '@' ? "\0" + path[1..] : path);
        _socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
    }

    /// <summary>Tells systemd the overlay's loop runs; nothing where no watchdog watches it.</summary>
    public void Tell()
    {
        if (_socket is null || _systemd is null)
        {
            return;
        }

        try
        {
            _socket.SendTo(Alive, _systemd);
            _failed = false;
        }
        catch (SocketException ex)
        {
            // Said once, not each second, until it goes through again.
            if (!_failed)
            {
                WatchdogUntold(_log, ex);
            }

            _failed = true;
        }
    }

    public void Dispose() => _socket?.Dispose();

    // systemd names the process it watches, which a program the overlay starts, inheriting its variables, is not.
    private static bool Watched()
    {
        var pid = Environment.GetEnvironmentVariable("WATCHDOG_PID");
        return Environment.GetEnvironmentVariable("WATCHDOG_USEC") is { Length: > 0 } && (string.IsNullOrEmpty(pid) || pid == $"{Environment.ProcessId}");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "systemd's watchdog could not be told the overlay runs")]
    private static partial void WatchdogUntold(ILogger logger, Exception error);
}
