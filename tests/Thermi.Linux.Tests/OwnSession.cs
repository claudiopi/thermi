using System.Diagnostics;
using Thermi.Client;

namespace Thermi.Linux.Tests;

/// <summary>
/// A desktop session of a test's own: its home, runtime folder, Wayland and X11 display names and D-Bus session bus, none
/// of them the person's, so nothing a test starts shows on their screen or talks to their desktop. Every process a Linux
/// test starts, a compositor and the SDK's <c>thermi-cli</c> among them, starts here, with the session's environment alone
/// and nothing of the test process's but its PATH, after the build's programs as a package puts them on it, and where .NET
/// runs from; a repo rule holds it. Ending the session ends all it started and deletes its folders.
/// </summary>
internal sealed class OwnSession : IAsyncDisposable
{
    /// <summary>The Wayland display's name, a socket in <see cref="RuntimeFolder"/> that the session's compositor makes.</summary>
    public const string WaylandDisplay = "wayland-thermi";

    /// <summary>What the tests' own program, started in a session, takes to answer as a program of the person's that reads through the SDK.</summary>
    public const string SdkStatus = "--sdk-status";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // X11 displays the person's server never takes: theirs are the first few. Each session takes the next.
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
    public IReadOnlyDictionary<string, string> Variables
    {
        get
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PATH"] = $"{AppContext.BaseDirectory.TrimEnd('/')}:{Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin"}",
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
            // The build's programs need .NET, which a PC may hold where only this variable says.
            if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnet)
            {
                variables["DOTNET_ROOT"] = dotnet;
            }

            return variables;
        }
    }

    /// <summary>
    /// Starts <paramref name="program"/> in the session, its session bus first if none runs yet, with
    /// <paramref name="environment"/> over the session's own; its output is read as it comes, and its input is the test's.
    /// </summary>
    public async Task<SessionProcess> StartAsync(
        string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancel = default)
    {
        if (!_busListens)
        {
            var bus = new SessionProcess(Launch("dbus-daemon", ["--session", "--nofork", "--nopidfile", "--print-address", $"--address={BusAddress}"], null));
            // It prints its address once it listens.
            await bus.ReadyAsync(cancel).ConfigureAwait(false);
            _busListens = true;
        }

        return new SessionProcess(Launch(program, args, environment));
    }

    /// <summary>Runs <paramref name="program"/> in the session to its end: its exit code, and what it wrote to each stream.</summary>
    public async Task<Ran> RunAsync(
        string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancel = default)
    {
        var process = await StartAsync(program, args, environment, cancel).ConfigureAwait(false);
        process.Input.Close();
        var output = process.Out.ToEndAsync(cancel);
        var error = process.Error.ToEndAsync(cancel);
        await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        return new Ran(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    /// <summary>
    /// What a program of the person's reads through the SDK, <c>new ThermiClient()</c>, which finds <c>thermi-cli</c> on the
    /// session's PATH and starts it with the session's environment: the tests' own program, started in the session with
    /// <see cref="SdkStatus"/>, reads it and says the service's state and whether an overlay runs.
    /// </summary>
    public async Task<(string Service, bool Overlay)> SdkStatusAsync(CancellationToken cancel)
    {
        var read = await RunAsync(Built.Program("Thermi.Linux.Tests"), [SdkStatus], cancel: cancel).ConfigureAwait(false);
        var said = read.Out.Trim().Split(' ');
        return read.Exit == 0 && said.Length == 2 ? (said[0], bool.Parse(said[1])) : throw new InvalidOperationException($"The SDK did not read: {read.Error}");
    }

    /// <summary>The tests' own program as <see cref="SdkStatusAsync"/> starts it: the SDK's status, on one line.</summary>
    public static async Task<int> AnswerAsTheSdkAsync()
    {
        var status = await new ThermiClient().StatusAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"{status.Service} {status.Overlay}").ConfigureAwait(false);
        return 0;
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

        // A user manager leaves folders no one may enter in its runtime folder, which only their owner may open again.
        Openable(_folder);
        _folder.Delete(recursive: true);
    }

    private static void Openable(DirectoryInfo folder)
    {
        folder.UnixFileMode |= OwnerOnly;
        foreach (var inner in folder.EnumerateDirectories().Where(inner => !inner.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            Openable(inner);
        }
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

/// <summary>How a program run in a session ended: its exit code, and what it wrote to each stream.</summary>
internal sealed record Ran(int Exit, string Out, string Error);
