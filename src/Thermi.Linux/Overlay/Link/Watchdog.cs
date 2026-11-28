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

    // What systemd hands the unit's main process alone. It takes a notice from that process only and logs any other's, and
    // systemctl, which the overlay runs to turn on start at sign-in, tells its exit on any socket it inherits.
    private static readonly string[] UnitOnly = ["NOTIFY_SOCKET", "WATCHDOG_PID", "WATCHDOG_USEC"];

    private readonly ILogger _log;
    private readonly Socket? _socket;
    private readonly UnixDomainSocketEndPoint? _systemd;
    private bool _failed;

    public Watchdog(ILogger log, Handed handed)
    {
        _log = log;
        // An abstract socket's name starts with "@", which the kernel's address writes as a NUL.
        if (handed.Socket is not { Length: > 0 } path || !Watched(handed))
        {
            return;
        }

        _systemd = new UnixDomainSocketEndPoint(path[0] == '@' ? "\0" + path[1..] : path);
        _socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
    }

    /// <summary>
    /// What systemd handed the overlay, taken out of the process's variables at its start, before it runs any program, so
    /// none inherits it: out of .NET's copy, which a process it starts gets, and out of the C library's, which GLib's get.
    /// </summary>
    public static Handed Take()
    {
        var handed = new Handed(
            Environment.GetEnvironmentVariable(UnitOnly[0]), Environment.GetEnvironmentVariable(UnitOnly[1]), Environment.GetEnvironmentVariable(UnitOnly[2]));
        // Before anything else of thermi's, which would have loaded GLib for it.
        GLib.Module.Initialize();
        foreach (var name in UnitOnly)
        {
            Environment.SetEnvironmentVariable(name, null);
            GLib.Functions.Unsetenv(name);
        }

        return handed;
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

    // systemd names the process it watches, which a program started by hand from the unit's environment is not.
    private static bool Watched(Handed handed) =>
        handed.Usec is { Length: > 0 } && (string.IsNullOrEmpty(handed.Pid) || handed.Pid == $"{Environment.ProcessId}");

    /// <summary>The notify socket, and the process and time the watchdog watches, as systemd handed them, or null.</summary>
    public sealed record Handed(string? Socket, string? Pid, string? Usec);

    [LoggerMessage(Level = LogLevel.Warning, Message = "systemd's watchdog could not be told the overlay runs")]
    private static partial void WatchdogUntold(ILogger logger, Exception error);
}
