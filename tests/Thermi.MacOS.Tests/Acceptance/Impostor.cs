namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// A server of another user's, nobody, which every Mac has: it serves recorded lines to whoever connects, as anyone may at a
/// path they can write, and A3's clients must refuse it. It listens in a folder of the test's own under <c>/private/tmp</c>,
/// which anyone may enter and add to and only its owner empty, as nobody cannot reach the session's own folders; it ends when
/// its input closes, as the session cannot end another user's process, and the folder goes with it.
/// </summary>
internal sealed class Impostor : IAsyncDisposable
{
    // Says it listens on its first line, and ends once its input closes, with whatever client it serves.
    private const string Script = """
        import os, socket, sys, threading
        def watch():
            sys.stdin.read()
            os._exit(0)
        threading.Thread(target=watch, daemon=True).start()
        server = socket.socket(socket.AF_UNIX)
        server.bind(sys.argv[1])
        os.chmod(sys.argv[1], 0o666)
        server.listen()
        print('listening', flush=True)
        lines = open(sys.argv[2], 'rb').read()
        while True:
            client, _ = server.accept()
            try:
                client.sendall(lines)
            except OSError:
                pass  # A client that refuses hangs up before the lines are all sent.
            client.close()
        """;

    private const UnixFileMode Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute | UnixFileMode.StickyBit;

    private const UnixFileMode Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private readonly string _folder;
    private readonly SessionProcess _server;

    private Impostor(string folder, SessionProcess server)
    {
        _folder = folder;
        _server = server;
    }

    /// <summary>Where it listens.</summary>
    public string Socket => Path.Combine(_folder, "sensors.sock");

    public static async Task<Impostor> StartAsync(OwnSession session, CancellationToken cancel)
    {
        var folder = Path.Combine("/private/tmp", $"thermi-impostor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder, Shared);
        File.SetUnixFileMode(folder, Shared);
        var script = Path.Combine(folder, "impostor.py");
        var lines = Path.Combine(folder, "lines.json");
        await File.WriteAllTextAsync(script, Script, cancel);
        File.Copy(Built.Recording(SensorFeed.MacMini) + ".json", lines);
        File.SetUnixFileMode(script, Readable);
        File.SetUnixFileMode(lines, Readable);
        var impostor = new Impostor(folder, session.Start("sudo", ["-n", "-u", "nobody", "python3", script, Path.Combine(folder, "sensors.sock"), lines]));
        try
        {
            await impostor._server.ReadyAsync(cancel);
            return impostor;
        }
        catch
        {
            await impostor.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _server.Input.Close();
            await _server.WaitForExitAsync(CancellationToken.None);
        }
        finally
        {
            // The folder is the test's, so its owner may empty it, sticky as it is.
            Directory.Delete(_folder, recursive: true);
        }
    }
}
