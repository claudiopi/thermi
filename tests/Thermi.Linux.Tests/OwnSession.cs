using System.Diagnostics;
using Thermi.Client;

namespace Thermi.Linux.Tests;

/// <summary>
/// A desktop session of a test's own: its home, runtime folder, Wayland and X11 display names and D-Bus session bus, none
/// of them the person's, so nothing a test starts shows on their screen or talks to their desktop. Every process a Linux
/// test starts, a compositor and the SDK's <c>thermi-cli</c> among them, starts here, with the session's environment alone
/// and nothing of the test process's but its PATH, after the build's programs as a package puts them on it, and where .NET
/// runs from; a repo rule holds it. Its bus starts only the services the session needs (<see cref="Activates"/>), so
/// several sessions at once fit in the inotify instances their user has. Ending the session ends all it started and deletes
/// its folders.
/// </summary>
internal sealed class OwnSession : IAsyncDisposable
{
    /// <summary>The Wayland display's name, a socket in <see cref="RuntimeFolder"/> that the session's compositor makes.</summary>
    public const string WaylandDisplay = "wayland-thermi";

    /// <summary>What the tests' own program, started in a session, takes to answer as a program of the person's that reads through the SDK.</summary>
    public const string SdkStatus = "--sdk-status";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // A session bus as the system's own, but for the services it may start, which are the session's alone; its limits are
    // a desktop's, past what the tests' monitors and the Shell ask.
    private const string BusConfig = """
        <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN"
         "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
        <busconfig>
          <type>session</type>
          <listen>LISTEN</listen>
          <keep_umask/>
          <auth>EXTERNAL</auth>
          <servicedir>SERVICES</servicedir>
          <policy context="default">
            <allow send_destination="*" eavesdrop="true"/>
            <allow eavesdrop="true"/>
            <allow own="*"/>
          </policy>
          <limit name="service_start_timeout">120000</limit>
          <limit name="max_match_rules_per_connection">50000</limit>
          <limit name="max_replies_per_connection">50000</limit>
        </busconfig>
        """;

    // X11 displays the person's server never takes: theirs are the first few. Each session takes the next.
    private static int _nextDisplay = 100 + (Environment.ProcessId % 400 * 64);

    // A desktop's bus starts any service a program asks for, file monitors, calendars, accounts and portals, each holding
    // inotify instances, of which a user has few: a session's bus starts only the settings' store, which gsettings writes
    // through, the accessibility bus, which the tests read menus and windows through, and what a test asks for.
    private static readonly string[] Always = ["ca.desrt.dconf", "org.a11y.Bus"];

    // The Shell starts ibus-daemon for input methods, which starts six programs of its own, and no test types through it:
    // in its place, a program of the same name that ends at once.
    private static readonly string[] StandIns = ["ibus-daemon"];

    private readonly DirectoryInfo _folder;
    private readonly string _services;
    private readonly string _standIns;
    private readonly List<Process> _started = [];
    private bool _busListens;

    public OwnSession()
    {
        _folder = Directory.CreateTempSubdirectory("thermi-session-");
        Home = _folder.CreateSubdirectory("home").FullName;
        // A runtime folder only its user may enter, as XDG's specification asks and the compositors check.
        RuntimeFolder = Directory.CreateDirectory(Path.Combine(_folder.FullName, "run"), OwnerOnly).FullName;
        X11Display = $":{Interlocked.Increment(ref _nextDisplay)}";
        _services = _folder.CreateSubdirectory("services").FullName;
        _standIns = _folder.CreateSubdirectory("stand-ins").FullName;
        foreach (var program in StandIns)
        {
            File.WriteAllText(Path.Combine(_standIns, program), "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(Path.Combine(_standIns, program), OwnerOnly);
        }

        Activates(Always);
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
                ["PATH"] = $"{AppContext.BaseDirectory.TrimEnd('/')}:{_standIns}:{Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin"}",
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
                // thermi reads the service's socket first, which a PC with thermi installed serves: the session's
                // programs read the session's own, which SensorFeed serves, alone.
                ["THERMI_SENSORS_SOCKET"] = Path.Combine(RuntimeFolder, "thermi", "sensors.sock"),
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
    /// <paramref name="environment"/> over the session's own, where a variable given empty is left out, as a session over SSH
    /// has no display's; its output is read as it comes, and its input is the test's.
    /// </summary>
    public async Task<SessionProcess> StartAsync(
        string program, IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancel = default)
    {
        if (!_busListens)
        {
            var config = Path.Combine(_folder.FullName, "bus.conf");
            var text = BusConfig.Replace("LISTEN", BusAddress, StringComparison.Ordinal).Replace("SERVICES", _services, StringComparison.Ordinal);
            await File.WriteAllTextAsync(config, text, cancel).ConfigureAwait(false);
            string[] daemon = [$"--config-file={config}", "--nofork", "--nopidfile", "--print-address"];
            var bus = new SessionProcess(Launch("dbus-daemon", daemon, null));
            // It prints its address once it listens.
            await bus.ReadyAsync(cancel).ConfigureAwait(false);
            _busListens = true;
        }

        return new SessionProcess(Launch(program, args, environment));
    }

    /// <summary>
    /// Lets the session's bus start the D-Bus services <paramref name="names"/> when a program asks for them, as a desktop's
    /// bus does, from where the PC installed them; one not installed stays unknown. A program that asks for any other goes on
    /// without it, as on a desktop that lacks it.
    /// </summary>
    public void Activates(params IEnumerable<string> names)
    {
        var shared = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share";
        foreach (var name in names)
        {
            var installed = shared.Split(':').Select(dir => Path.Combine(dir, "dbus-1", "services", $"{name}.service")).FirstOrDefault(File.Exists);
            if (installed is not null)
            {
                File.Copy(installed, Path.Combine(_services, $"{name}.service"), overwrite: true);
            }
        }
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

        // A FUSE file system the session's bus started, the document portal's or gvfs's, outlives its killed daemon as a
        // folder no one may open, which only its unmount lets go.
        foreach (var mount in FuseMounts())
        {
            using var unmount = Launch("fusermount3", ["-u", "-z", mount], null);
            await unmount.WaitForExitAsync().ConfigureAwait(false);
        }

        // A user manager leaves folders no one may enter in its runtime folder, which only their owner may open again.
        Openable(_folder);
        _folder.Delete(recursive: true);
    }

    // Each line of mountinfo is its mount's fields, its mount point the fifth, then "-" and its file system's type.
    private List<string> FuseMounts() => [.. File.ReadLines("/proc/self/mountinfo")
        .Select(line => line.Split(' '))
        .Where(fields => fields[Array.IndexOf(fields, "-") + 1].StartsWith("fuse", StringComparison.Ordinal))
        .Select(fields => fields[4])
        .Where(mount => mount.StartsWith(_folder.FullName + "/", StringComparison.Ordinal))];

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

        foreach (var (name, _) in environment?.Where(variable => variable.Value.Length == 0) ?? [])
        {
            start.Environment.Remove(name);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        _started.Add(process);
        return process;
    }
}

/// <summary>How a program run in a session ended: its exit code, and what it wrote to each stream.</summary>
internal sealed record Ran(int Exit, string Out, string Error);
