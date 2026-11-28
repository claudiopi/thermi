using System.Globalization;
using System.Net.Sockets;

namespace Thermi.Sensors.Linux.Serving;

/// <summary>The socket systemd hands its service, which Core's <c>SocketServer</c> serves.</summary>
internal static class Systemd
{
    // The first socket systemd hands a service, after standard input, output and error.
    private const int FirstHanded = 3;

    /// <summary>The socket systemd listens on and handed this process, as its socket unit says, or null when it handed none.</summary>
    public static Socket? Socket() =>
        Environment.GetEnvironmentVariable("LISTEN_PID") == Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            && Environment.GetEnvironmentVariable("LISTEN_FDS") == "1"
#pragma warning disable CA2000 // The socket owns the handle, and disposes it with itself.
            ? new Socket(new SafeSocketHandle(FirstHanded, ownsHandle: true))
#pragma warning restore CA2000
            : null;
}
