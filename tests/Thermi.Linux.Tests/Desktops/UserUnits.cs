using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A systemd user manager of the session's own, on its bus and in its folders, which runs thermi's user unit as a person's
/// sign-in does: the unit as the package installs it, with the program the build put beside the tests, its trace in
/// <see cref="Trace"/> across the unit's restarts, so a test waits for what thermi does, never for a time.
/// </summary>
internal sealed partial class UserUnits
{
    /// <summary>The overlay's user unit as the package installs it.</summary>
    public static readonly string Unit = Path.Combine(Built.Root, "src", "Thermi.Linux", "Packaging", "thermi.service");

    // Says "listening" once it listens where the manager says it is ready, then "ready" when it does.
    private const string Readiness = """
        import socket, sys
        listener = socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM)
        listener.bind(sys.argv[1])
        print('listening', flush=True)
        while True:
            if 'READY=1' in listener.recv(4096).decode().split('\n'):
                print('ready', flush=True)
        """;

    private readonly OwnSession _session;

    private UserUnits(OwnSession session, Lines trace)
    {
        _session = session;
        Trace = trace;
    }

    /// <summary>What the unit's thermi writes under <c>--trace</c>, each start's after the last's.</summary>
    public Lines Trace { get; }

    /// <summary>
    /// The session's user manager, once it is ready. A manager makes a cgroup for each unit, which it may only in a cgroup
    /// delegated to it: the person's own manager makes one, a scope of the session's, and runs the session's manager in it,
    /// which touches nothing else of theirs and ends with the session.
    /// </summary>
    public static async Task<UserUnits> StartAsync(OwnSession session, CancellationToken cancel, string? program = null)
    {
        var units = Directory.CreateDirectory(Path.Combine(session.Variables["XDG_CONFIG_HOME"], "systemd", "user")).FullName;
        File.Copy(Unit, Path.Combine(units, "thermi.service"));
        var fifo = Path.Combine(session.Home, "trace");
        var made = await session.RunAsync("mkfifo", [fifo], cancel: cancel).ConfigureAwait(false);
        _ = made.Exit == 0 ? 0 : throw new InvalidOperationException($"No pipe for the unit's trace: {made.Error}");
        var dropIn = Directory.CreateDirectory(Path.Combine(units, "thermi.service.d")).FullName;
        var service = $"[Service]\nExecStart=\nExecStart={program ?? Built.Program("thermi")} --trace\nStandardError=file:{fifo}\n";
        await File.WriteAllTextAsync(Path.Combine(dropIn, "built.conf"), service, cancel).ConfigureAwait(false);
        // Each start of the unit opens the pipe again, and each end closes it: cat reads it from each open to that close.
        var trace = await session.StartAsync("sh", ["-c", "while :; do cat \"$0\"; done", fifo], cancel: cancel).ConfigureAwait(false);

        var notify = Path.Combine(session.RuntimeFolder, "manager-notify");
        var script = Path.Combine(session.Home, "readiness.py");
        await File.WriteAllTextAsync(script, Readiness, cancel).ConfigureAwait(false);
        var readiness = await session.StartAsync("python3", [script, notify], cancel: cancel).ConfigureAwait(false);
        await readiness.ReadyAsync(cancel).ConfigureAwait(false);
        var ready = readiness.Out.NextAsync(line => line == "ready", cancel);
        var person = await PersonsRuntimeFolderAsync(cancel).ConfigureAwait(false);
        await session.StartAsync(
            "systemd-run",
            ["--user", "--scope", "--quiet", "--property=Delegate=yes", "env", $"XDG_RUNTIME_DIR={session.RuntimeFolder}",
                $"DBUS_SESSION_BUS_ADDRESS={session.BusAddress}", $"NOTIFY_SOCKET={notify}", "/usr/lib/systemd/systemd", "--user"],
            new Dictionary<string, string> { ["XDG_RUNTIME_DIR"] = person, ["DBUS_SESSION_BUS_ADDRESS"] = $"unix:path={person}/bus" },
            cancel).ConfigureAwait(false);
        await ready.ConfigureAwait(false);
        return new UserUnits(session, trace.Out);
    }

    /// <summary>What <c>systemctl --user</c> says, with <paramref name="args"/>.</summary>
    public async Task<string> SystemctlAsync(IEnumerable<string> args, CancellationToken cancel) =>
        (await _session.RunAsync("systemctl", ["--user", .. args], cancel: cancel).ConfigureAwait(false)).Out.Trim();

    /// <summary>
    /// Starts the overlay's unit, and returns what ends once systemd says the unit went inactive: <c>systemctl --wait</c>,
    /// which follows the unit's own state.
    /// </summary>
    public Task<SessionProcess> StartWaitingAsync(CancellationToken cancel) =>
        _session.StartAsync("systemctl", ["--user", "start", "--wait", "thermi.service"], cancel: cancel);

    /// <summary>The processes of the overlay's unit, by their ids.</summary>
    public async Task<IReadOnlyList<string>> ProcessesAsync(CancellationToken cancel)
    {
        var group = await SystemctlAsync(["show", "-p", "ControlGroup", "--value", "thermi.service"], cancel).ConfigureAwait(false);
        var procs = Path.Combine("/sys/fs/cgroup" + group, "cgroup.procs");
        return File.Exists(procs) ? (await File.ReadAllLinesAsync(procs, cancel).ConfigureAwait(false)).Where(line => line.Length > 0).ToList() : [];
    }

    private static async Task<string> PersonsRuntimeFolderAsync(CancellationToken cancel)
    {
        var status = await File.ReadAllTextAsync("/proc/self/status", cancel).ConfigureAwait(false);
        return $"/run/user/{int.Parse(Uid().Match(status).Groups["id"].Value, CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex(@"^Uid:\s+(?<id>\d+)", RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Uid();
}
