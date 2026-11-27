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

    // The scope each manager runs in, by the test process's id: thermi-tests-<pid>-<guid>.scope.
    private const string Scope = "thermi-tests-";

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
    /// delegated to it: the person's own manager makes one, a scope named for the test process, and runs the session's
    /// manager in it, which loads the tests' own units alone and ends with the session.
    /// </summary>
    public static async Task<UserUnits> StartAsync(OwnSession session, CancellationToken cancel, string? program = null)
    {
        var units = Directory.CreateDirectory(Path.Combine(session.Variables["XDG_CONFIG_HOME"], "systemd", "user")).FullName;
        var fifo = await WriteUnitsAsync(session, units, program ?? Built.Program("thermi"), cancel).ConfigureAwait(false);
        // Each start of the unit opens the pipe again, and each end closes it: cat reads it from each open to that close.
        var trace = await session.StartAsync("sh", ["-c", "while :; do cat \"$0\"; done", fifo], cancel: cancel).ConfigureAwait(false);

        var notify = Path.Combine(session.RuntimeFolder, "manager-notify");
        var ready = await Readiness.ListenAsync(session, notify, cancel).ConfigureAwait(false);
        var person = new Dictionary<string, string> { ["XDG_RUNTIME_DIR"] = await PersonsRuntimeFolderAsync(cancel).ConfigureAwait(false) };
        await StopLeftAsync(session, person, cancel).ConfigureAwait(false);
        // Only the tests' own units, and no generator's, which would make units of the person's autostart; its log goes to
        // the test's output, never the person's journal, and its units' output nowhere but where they say.
        var none = Directory.CreateDirectory(Path.Combine(session.Home, "no-generators")).FullName;
        string[] manager =
        [
            $"XDG_RUNTIME_DIR={session.RuntimeFolder}", $"DBUS_SESSION_BUS_ADDRESS={session.BusAddress}", $"NOTIFY_SOCKET={notify}",
            $"SYSTEMD_UNIT_PATH={units}", $"SYSTEMD_GENERATOR_PATH={none}", $"SYSTEMD_ENVIRONMENT_GENERATOR_PATH={none}",
            "/usr/lib/systemd/systemd", "--user", "--log-target=console", "--default-standard-output=null",
        ];
        // A manager that loads no exit.target cannot end on SIGTERM: stopping its scope kills it, and its units with it.
        string[] scope =
        [
            "--user", "--scope", "--quiet", "--property=Delegate=yes", "--property=KillSignal=SIGKILL",
            $"--unit={Scope}{Environment.ProcessId}-{Guid.NewGuid():N}",
        ];
        var started = await session.StartAsync("systemd-run", [.. scope, "env", .. manager], person, cancel).ConfigureAwait(false);
        // The scope runs the manager in systemd-run's own process.
        if (!await Readiness.BeforeEndAsync(ready, started.WaitForExitAsync(cancel)).ConfigureAwait(false))
        {
            var said = await started.Error.ToEndAsync(cancel).ConfigureAwait(false);
            throw new InvalidOperationException($"The tests' user manager ended before it was ready: {said}");
        }

        // A manager sets its units' PATH itself; a desktop's session hands it its own, as here, the tests' programs first,
        // and a unit finds a program as a person's does.
        var programs = Directory.CreateDirectory(ProgramsOf(session)).FullName;
        var userUnits = new UserUnits(session, trace.Out);
        await userUnits.SystemctlAsync(["set-environment", $"PATH={programs}:{Environment.GetEnvironmentVariable("PATH")}"], cancel).ConfigureAwait(false);
        // A manager joins the session's bus once its bus units run, as a person's does with the bus's own: the tests' stand
        // for them, the session's bus being the test's. On the bus, it tells systemctl --wait when a unit ends.
        await userUnits.SystemctlAsync(["start", "dbus.socket", "dbus.service"], cancel).ConfigureAwait(false);
        // A desktop's sign-in starts its graphical session, which thermi's unit asks to be up before it starts.
        await userUnits.SystemctlAsync(["start", "graphical-session.target"], cancel).ConfigureAwait(false);
        string[] wait = ["wait", "--session", "--timeout", Bound.LineSeconds, "org.freedesktop.systemd1"];
        var joined = await session.RunAsync("gdbus", wait, cancel: cancel).ConfigureAwait(false);
        _ = joined.Exit == 0 ? 0 : throw new InvalidOperationException($"The tests' user manager never joined the bus: {joined.Error}");
        return userUnits;
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

    /// <summary>
    /// A program of the desktop's, as its system monitor, that the units find first on their PATH: once started, it takes its
    /// name on the session's bus, as the real one does, and holds it until the session ends. Nothing is installed.
    /// </summary>
    public async Task AddProgramAsync(string program, string busName, CancellationToken cancel)
    {
        var path = Path.Combine(ProgramsOf(_session), program);
        var code = "#!/usr/bin/env python3\nfrom gi.repository import Gio, GLib\n"
            + $"Gio.bus_own_name(Gio.BusType.SESSION, '{busName}', Gio.BusNameOwnerFlags.NONE, None, None, None)\nGLib.MainLoop().run()\n";
        await File.WriteAllTextAsync(path, code, cancel).ConfigureAwait(false);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>The processes of the overlay's unit, by their ids.</summary>
    public async Task<IReadOnlyList<string>> ProcessesAsync(CancellationToken cancel)
    {
        var group = await SystemctlAsync(["show", "-p", "ControlGroup", "--value", "thermi.service"], cancel).ConfigureAwait(false);
        var procs = Path.Combine("/sys/fs/cgroup" + group, "cgroup.procs");
        return File.Exists(procs) ? (await File.ReadAllLinesAsync(procs, cancel).ConfigureAwait(false)).Where(line => line.Length > 0).ToList() : [];
    }

    // The overlay's unit as the package installs it, running the build's program with its trace on a pipe; and the targets
    // a unit needs or is wanted by, which the manager holds no other copy of. Nothing of /usr/lib/systemd/user or
    // /etc/systemd/user loads, so nothing of the person's starts twice: their audio, their Bluetooth, the bus of their own.
    private static async Task<string> WriteUnitsAsync(OwnSession session, string units, string program, CancellationToken cancel)
    {
        File.Copy(Unit, Path.Combine(units, "thermi.service"));
        foreach (var target in new[] { "default.target", "basic.target", "graphical-session.target" })
        {
            await File.WriteAllTextAsync(Path.Combine(units, target), "[Unit]\nDescription=thermi's tests\n", cancel).ConfigureAwait(false);
        }

        // The manager takes the session's bus as up while a dbus.socket listens and a dbus.service runs: these stand for them,
        // listening where no one calls and running a program that waits for nothing.
        await File.WriteAllTextAsync(Path.Combine(units, "dbus.socket"), "[Socket]\nListenStream=%t/tests-bus-stand-in\n", cancel)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(units, "dbus.service"), "[Service]\nExecStart=/usr/bin/tail -f /dev/null\n", cancel)
            .ConfigureAwait(false);

        var fifo = Path.Combine(session.Home, "trace");
        var made = await session.RunAsync("mkfifo", [fifo], cancel: cancel).ConfigureAwait(false);
        _ = made.Exit == 0 ? 0 : throw new InvalidOperationException($"No pipe for the unit's trace: {made.Error}");
        var dropIn = Directory.CreateDirectory(Path.Combine(units, "thermi.service.d")).FullName;
        var service = $"[Service]\nExecStart=\nExecStart={program} --trace\nStandardError=file:{fifo}\n";
        await File.WriteAllTextAsync(Path.Combine(dropIn, "built.conf"), service, cancel).ConfigureAwait(false);
        return fifo;
    }
    // A run of the tests that died leaves its scope behind, with the manager and the units in it: those whose test process
    // is gone end here. A run still going keeps its own.
    private static async Task StopLeftAsync(OwnSession session, IReadOnlyDictionary<string, string> person, CancellationToken cancel)
    {
        var listed = await session.RunAsync(
            "systemctl", ["--user", "list-units", "--all", "--type=scope", "--plain", "--no-legend", $"{Scope}*"], person, cancel)
            .ConfigureAwait(false);
        var left = listed.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ')[0])
            .Where(scope => Left().Match(scope) is { Success: true } match && !Directory.Exists($"/proc/{match.Groups["test"].Value}"))
            .ToList();
        if (left.Count > 0)
        {
            await session.RunAsync("systemctl", ["--user", "stop", .. left], person, cancel).ConfigureAwait(false);
            // One whose manager was killed stays listed as failed until it is reset.
            await session.RunAsync("systemctl", ["--user", "reset-failed", .. left], person, cancel).ConfigureAwait(false);
        }
    }

    private static string ProgramsOf(OwnSession session) => Path.Combine(session.Home, "programs");

    private static async Task<string> PersonsRuntimeFolderAsync(CancellationToken cancel)
    {
        var status = await File.ReadAllTextAsync("/proc/self/status", cancel).ConfigureAwait(false);
        return $"/run/user/{int.Parse(Uid().Match(status).Groups["id"].Value, CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex(@"^thermi-tests-(?<test>\d+)-[0-9a-f]{32}\.scope$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Left();

    [GeneratedRegex(@"^Uid:\s+(?<id>\d+)", RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Uid();
}
