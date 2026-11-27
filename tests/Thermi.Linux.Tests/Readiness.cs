namespace Thermi.Linux.Tests;

/// <summary>
/// A socket systemd says it is ready on, as it tells whoever started it once its start is done: a user manager, or a
/// container's first process once it booted. A test waits for that word, never for a time.
/// </summary>
internal static class Readiness
{
    // Says "listening" once it listens, then "ready" for the first READY=1 that comes.
    private const string Code = """
        import socket, sys
        listener = socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM)
        listener.bind(sys.argv[1])
        print('listening', flush=True)
        while 'READY=1' not in listener.recv(4096).decode().split('\n'):
            pass
        print('ready', flush=True)
        """;

    /// <summary>Listens on <paramref name="socket"/>, a path for <c>NOTIFY_SOCKET</c>, and returns what ends once systemd says it is ready there.</summary>
    public static async Task<Task> ListenAsync(OwnSession session, string socket, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "readiness.py");
        if (!File.Exists(script))
        {
            await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        }

        var listener = await session.StartAsync("python3", [script, socket], cancel: cancel).ConfigureAwait(false);
        await listener.ReadyAsync(cancel).ConfigureAwait(false);
        return listener.Out.NextAsync(line => line == "ready", cancel);
    }
}
