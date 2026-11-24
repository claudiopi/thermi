using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// An update server on this PC's loopback, as plain as R2 behind its cache: it answers each GET with the file at that
/// path or 404, offering a cookie a client must never send back, and keeps every request exactly as it came over the wire.
/// </summary>
internal sealed class LocalChannel : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly List<string> _requests = [];
    private readonly Lock _lock = new();
    private readonly Task _serving;

    public LocalChannel()
    {
        _listener.Start();
        _serving = ServeAsync(_stop.Token);
    }

    public Uri Address => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1/");

    /// <summary>Every request's head, its request line and headers, in the order they came.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The request lines' paths, as <c>/v1/stable.json</c>.</summary>
    public IReadOnlyList<string> Paths => [.. Requests.Select(request => request.Split(' ')[1])];

    public void Serve(string path, byte[] body)
    {
        lock (_lock)
        {
            _files[path] = body;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Dispose();
#pragma warning disable VSTHRD003 // Started in the constructor, and ended by the cancel just above.
        await _serving;
#pragma warning restore VSTHRD003
        _stop.Dispose();
    }

    private async Task ServeAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(stop);
                await AnswerAsync(client.GetStream(), stop);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The test is over.
        }
    }

    private async Task AnswerAsync(NetworkStream stream, CancellationToken stop)
    {
        var head = new StringBuilder();
        var buffer = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(buffer, stop) > 0)
        {
            head.Append((char)buffer[0]);
        }

        var request = head.ToString();
        byte[]? body;
        lock (_lock)
        {
            _requests.Add(request);
            _files.TryGetValue(request.Split(' ').ElementAtOrDefault(1) ?? "", out body);
        }

        var status = body is null ? "404 Not Found" : "200 OK";
        var answer = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body?.Length ?? 0}\r\nSet-Cookie: visitor=1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(answer, stop);
        await stream.WriteAsync(body ?? [], stop);
    }
}
