using System.Net.Sockets;
using Thermi.App.Sensors;

namespace Thermi.Linux.Sensors;

/// <summary>
/// The sensor service's socket (docs/adr/0019-linux.md): the person's own, in their runtime folder, first, then the
/// system's. Only its owner writes either folder, the person or root, so whatever answers there is thermi's service.
/// </summary>
internal sealed class SensorSocket(TimeProvider time) : ISensorSource
{
    /// <summary>The socket's name under a runtime folder, the person's or the system's <c>/run</c>.</summary>
    public const string Name = "thermi/sensors.sock";

    private const string SystemRun = "/run";

    public async Task<Stream> OpenAsync(CancellationToken stop)
    {
        IOException? none = null;
        foreach (var path in Paths())
        {
            try
            {
                return await ConnectAsync(path, stop);
            }
            catch (SocketException ex)
            {
                none = new IOException($"{path}: {ex.Message}", ex);
            }
        }

        throw none ?? new IOException("There is no runtime folder to find the sensors' socket in.");
    }
    public void Missing()
    {
        // systemd starts the service when its socket is first read.
    }

    private async Task<Stream> ConnectAsync(string path, CancellationToken stop)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var timeout = new CancellationTokenSource(SensorReader.Retry, time);
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), connecting.Token);
            var stream = new NetworkStream(socket, ownsSocket: true);
            socket = null;
            return stream;
        }
        catch (OperationCanceledException ex) when (!stop.IsCancellationRequested)
        {
            throw new TimeoutException($"{path} did not answer in time.", ex);
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private static IEnumerable<string> Paths()
    {
        if (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime)
        {
            yield return Path.Combine(runtime, Name);
        }

        yield return Path.Combine(SystemRun, Name);
    }
}
