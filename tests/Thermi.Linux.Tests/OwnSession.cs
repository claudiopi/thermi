using System.Diagnostics;

namespace Thermi.Linux.Tests;

/// <summary>
/// A desktop session of a test's own: its home, runtime folder, Wayland and X11 display names and D-Bus session bus, none
/// of them the person's, so nothing a test starts shows on their screen or talks to their desktop. Every process a Linux
/// test starts, a compositor among them, starts here, with the session's environment alone and nothing of the test
/// process's but its PATH; a repo rule holds it. Ending the session ends all it started and deletes its folders.
/// </summary>
internal sealed class OwnSession : IAsyncDisposable
{
    // X11 displays the person's server never takes: theirs are the first few. Each session takes the next.
    /// <summary>The Wayland display's name, a socket in <see cref="RuntimeFolder"/> that the session's compositor makes.</summary>
    public const string WaylandDisplay = "wayland-thermi";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static int _nextDisplay = 100 + (Environment.ProcessId % 400 * 64);

    private readonly DirectoryInfo _folder;
    private readonly List<Process> _started = [];
    private bool _busListens;

    public OwnSession()
    {
        _folder = Directory.CreateTempSubdirectory("thermi-session-");
        Home = _folder.CreateSubdirectory("home").FullName;
        // A runtime folder only its user may enter, as XDG's specification asks and the compositors check.
        RuntimeFolder = Directory.CreateDirectory(Path.Combine(_folder.FullName, "run"), OwnerOnly).FullName;
        X11Display = $":{Interlocked.Increment(ref _nextDisplay)}";
    }

    public string Home { get; }

    public string RuntimeFolder { get; }

    /// <summary>The X11 display's name, which the session's X server takes; one that says which it took is set to it.</summary>
    public string X11Display { get; set; }

    public string BusAddress => $"unix:path={Path.Combine(RuntimeFolder, "bus")}";

    /// <summary>The environment every process of the session starts with.</summary>
    public IReadOnlyDictionary<string, string> Variables => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin",
        ["HOME"] = Home,
        ["XDG_RUNTIME_DIR"] = RuntimeFolder,
        ["XDG_CONFIG_HOME"] = Path.Combine(Home, ".config"),
        ["XDG_DATA_HOME"] = Path.Combine(Home, ".local", "share"),
        ["XDG_STATE_HOME"] = Path.Combine(Home, ".local", "state"),
        ["XDG_CACHE_HOME"] = Path.Combine(Home, ".cache"),
        ["WAYLAND_DISPLAY"] = WaylandDisplay,
        ["DISPLAY"] = X11Display,
        ["DBUS_SESSION_BUS_ADDRESS"] = BusAddress,
        ["LANG"] = "C.UTF-8",
    };

    /// <summary>
    /// Starts <paramref name="program"/> in the session, its session bus first if none runs yet, with
    /// <paramref name="environment"/> over the session's own; its standard streams are the test's to read and write.
    /// </summary>
    public async Task<Process> StartAsync(
        string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancel = default)
    {
        if (!_busListens)
        {
            var bus = Launch("dbus-daemon", ["--session", "--nofork", "--nopidfile", "--print-address", $"--address={BusAddress}"], null);
            // It prints its address once it listens.
            _busListens = await bus.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false) is not null
                ? true
                : throw new InvalidOperationException($"The session's bus ended: {await bus.StandardError.ReadToEndAsync(cancel).ConfigureAwait(false)}");
        }

        return Launch(program, args, environment);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in Enumerable.Reverse(_started))
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            process.Dispose();
        }

        _folder.Delete(recursive: true);
    }

    private Process Launch(string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Home,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment.Clear();
        foreach (var (name, value) in Variables.Concat(environment ?? new Dictionary<string, string>()))
        {
            start.Environment[name] = value;
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        _started.Add(process);
        return process;
    }
}
