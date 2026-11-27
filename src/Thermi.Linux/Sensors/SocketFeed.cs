using System.Globalization;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Thermi.App.Sensors;
using Thermi.Core.Pipe;
using Thermi.Core.Sensors;

namespace Thermi.Linux.Sensors;

/// <summary>
/// Reads <c>thermi-sensors</c>' socket (<see cref="SensorReader"/>): the service's, and only where it is missing or
/// refuses, the person's own in their runtime folder, which <c>thermi-sensors --console</c> serves; a socket file left by
/// one that ended refuses, and is passed over. Anyone may serve a socket at a path they can write while the service is
/// stopped, so the service's is read only when the kernel says, by <c>SO_PEERCRED</c>, that root or the service's user
/// listens on it; the person's own, in a folder only they and root write, when they or root do. <see cref="Variable"/>
/// names the one socket to read in their place, trusted as the person's own: a test's session, or a person's console.
/// </summary>
internal sealed class SocketFeed(TimeProvider time, ILogger log) : SensorReader(time, log)
{
    /// <summary>The service's user, as its package creates it.</summary>
    public const string ServiceUser = "thermi";

    /// <summary>The variable that names the one socket to read, as a test's session sets it to its own.</summary>
    public const string Variable = "THERMI_SENSORS_SOCKET";

    private const uint Root = 0;

    /// <summary>Whether the service is installed: its socket unit is there, which systemd listens on.</summary>
    public static bool Installed() =>
        File.Exists("/usr/lib/systemd/system/thermi-sensors.socket") || File.Exists("/etc/systemd/system/thermi-sensors.socket");

    protected override async Task<(Stream Connection, bool Ours)> ConnectAsync(CancellationToken stop)
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } chosen)
        {
            return await ConnectAsync(chosen, [OwnUserId(), Root], stop);
        }

        try
        {
            return await ConnectAsync(SnapshotJson.SocketPath, [Root, UserId(ServiceUser)], stop);
        }
        catch (IOException) when (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime)
        {
            return await ConnectAsync(Path.Combine(runtime, SnapshotJson.RuntimeSocket), [OwnUserId(), Root], stop);
        }
    }

    private async Task<(Stream Connection, bool Ours)> ConnectAsync(string path, IReadOnlyList<uint?> trusted, CancellationToken stop)
    {
#pragma warning disable CA2000 // The stream it answers owns it, and each way out that answers none disposes it.
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
#pragma warning restore CA2000
        try
        {
            using var timeout = new CancellationTokenSource(Retry, Time);
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), connecting.Token);
            var peer = PeerCredentials.UserOf(socket);
            return (new NetworkStream(socket, ownsSocket: true), peer is not null && trusted.Contains(peer));
        }
        catch (OperationCanceledException ex) when (!stop.IsCancellationRequested)
        {
            socket.Dispose();
            throw new TimeoutException("The sensors' socket did not answer in time.", ex);
        }
        catch (SocketException ex)
        {
            // Nothing listens there: the service is stopped or not installed.
            socket.Dispose();
            throw new IOException($"No sensor service listens on {path}: {ex.SocketErrorCode}.", ex);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The user this process acts as, its effective user id from <c>/proc/self/status</c>, or null where it cannot be read.</summary>
    public static uint? OwnUserId()
    {
        // Uid: real, effective, saved and file system ids.
        try
        {
            return File.ReadLines("/proc/self/status").FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal)) is { } line
                && line.Split('\t', StringSplitOptions.RemoveEmptyEntries) is [_, _, var effective, ..]
                && uint.TryParse(effective, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A user's id from /etc/passwd, where systemd-sysusers, as the package asks, writes the service's.
    private static uint? UserId(string user)
    {
        try
        {
            return File.ReadLines("/etc/passwd").Select(line => line.Split(':')).FirstOrDefault(fields => fields.Length > 2 && fields[0] == user) is { } entry
                && uint.TryParse(entry[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
