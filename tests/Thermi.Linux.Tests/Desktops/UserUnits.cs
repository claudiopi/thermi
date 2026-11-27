namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A systemd user manager of the session's own, on its bus and in its folders, which runs thermi's user unit as a person's
/// sign-in does: the unit as the package installs it, with the program the build put beside the tests, tracing each paint,
/// a hidden overlay's too, to a pipe the test reads, so it waits for what thermi shows, never for a time.
/// </summary>
internal sealed class UserUnits : IDisposable
{
    /// <summary>The overlay's user unit as the package installs it.</summary>
    public static readonly string Unit = Path.Combine(Built.Root, "src", "Thermi.Linux", "Packaging", "thermi.service");

    private readonly OwnSession _session;
    private readonly string _trace;
    private StreamReader? _reader;

    private UserUnits(OwnSession session, string trace)
    {
        _session = session;
        _trace = trace;
    }

    public static async Task<UserUnits> StartAsync(OwnSession session, CancellationToken cancel, string? program = null)
    {
        var units = Directory.CreateDirectory(Path.Combine(session.Variables["XDG_CONFIG_HOME"], "systemd", "user")).FullName;
        File.Copy(Unit, Path.Combine(units, "thermi.service"));
        var trace = Path.Combine(session.Home, "trace");
        var made = await session.RunAsync("mkfifo", [trace], cancel: cancel).ConfigureAwait(false);
        var dropIn = Directory.CreateDirectory(Path.Combine(units, "thermi.service.d")).FullName;
        var service = $"[Service]\nExecStart=\nExecStart={program ?? Built.Program("thermi")} --trace\nStandardError=file:{trace}\n";
        await File.WriteAllTextAsync(Path.Combine(dropIn, "built.conf"), service, cancel).ConfigureAwait(false);
        await session.StartAsync("/usr/lib/systemd/systemd", ["--user"], cancel: cancel).ConfigureAwait(false);
        var up = await session.RunAsync("gdbus", ["wait", "--session", "org.freedesktop.systemd1"], cancel: cancel).ConfigureAwait(false);
        return made.Exit == 0 && up.Exit == 0
            ? new UserUnits(session, trace)
            : throw new InvalidOperationException($"The user manager never came up: {made.Error}{up.Error}");
    }

    /// <summary>What <c>systemctl --user</c> says, with <paramref name="args"/>.</summary>
    public async Task<string> SystemctlAsync(IEnumerable<string> args, CancellationToken cancel) =>
        (await _session.RunAsync("systemctl", ["--user", .. args], cancel: cancel).ConfigureAwait(false)).Out.Trim();

    /// <summary>The processes of the overlay's unit, by their ids.</summary>
    public async Task<IReadOnlyList<string>> ProcessesAsync(CancellationToken cancel)
    {
        var group = await SystemctlAsync(["show", "-p", "ControlGroup", "--value", "thermi.service"], cancel).ConfigureAwait(false);
        var procs = Path.Combine("/sys/fs/cgroup" + group, "cgroup.procs");
        return File.Exists(procs) ? (await File.ReadAllLinesAsync(procs, cancel).ConfigureAwait(false)).Where(line => line.Length > 0).ToList() : [];
    }

    /// <summary>Returns once the unit's thermi painted next, across its restarts: each one opens the pipe anew.</summary>
    public async Task PaintedAsync(CancellationToken cancel)
    {
        while (true)
        {
            // A pipe opens for reading once a writer opens it, which a restarted thermi does.
            _reader ??= await Task.Run(() => new StreamReader(_trace), cancel).ConfigureAwait(false);
            var line = await _reader.ReadLineAsync(cancel).ConfigureAwait(false);
            if (line is null)
            {
                _reader.Dispose();
                _reader = null;
            }
            else if (line.EndsWith("paint", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Watches the unit from now, and returns what waits until systemd says it went inactive: the unit's own signal, so the
    /// test then reads the state systemd settled on.
    /// </summary>
    public async Task<Func<Task>> WatchStopAsync(CancellationToken cancel)
    {
        var monitor = await _session.StartAsync(
            "gdbus", ["monitor", "--session", "--dest", "org.freedesktop.systemd1", "--object-path", "/org/freedesktop/systemd1/unit/thermi_2eservice"],
            cancel: cancel).ConfigureAwait(false);
        // gdbus says it monitors before the first signal.
        await monitor.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false);
        return async () =>
        {
            while (await monitor.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
            {
                if (line.Contains("'ActiveState': <'inactive'>", StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new InvalidOperationException("The unit never went inactive.");
        };
    }

    public void Dispose() => _reader?.Dispose();
}
