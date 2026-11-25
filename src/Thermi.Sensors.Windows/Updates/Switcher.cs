using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Core.Sensors;
using Thermi.Core.Updates;
using Windows.Win32;

namespace Thermi.Sensors.Windows.Updates;

/// <summary>
/// Moves the PC from one version to another (docs/adr/0008-updates.md). The service writes the switch down and starts a
/// watchdog, <c>thermi-sensors --switch &lt;version&gt;</c>, its own for a step forward and the older version's for a step
/// back, which outlives it: the watchdog stops the
/// service, points it at the new version, starts it and points it back at once, so a power cut leaves Windows starting the
/// old version; then it polls the pipe, and points both services at the new version for good only once it serves.
/// </summary>
internal sealed class Switcher(
    InstallFolder folder,
    TimeProvider time,
    Action<string, EventLogEntryType> say,
    Action<ProcessStartInfo>? start = null,
    Action<ReleaseVersion>? shown = null,
    Func<InstallFolder, ISwitchTarget>? target = null)
{
    public const string Option = "--switch";

    private const int WriteTries = 5;

    // ERROR_SERVICE_REQUEST_TIMEOUT: the service did not answer Windows in time.
    private const int ServiceTimedOut = 1053;

    // ERROR_SHUTDOWN_IN_PROGRESS: Windows refused the start because it is shutting down, which says nothing of the version,
    // so it is held without counting as a cut.
    private const int ShuttingDown = 1115;

    private readonly SwitchLock _lock = new(folder, time);

    private readonly ISwitchTarget _target = (target ?? (each => new ServiceTarget(each)))(folder);

    /// <summary>Whether a watchdog is switching now, so the state is its own until it is done.</summary>
    public bool UnderWay() => _lock.Held();
    /// <summary>
    /// Writes the switch to <paramref name="to"/> down and starts its watchdog: this version's for a step forward, and for
    /// a step back after a failed probation the version it goes back to, which ran here before.
    /// </summary>
    public void Begin(UpdateState state, ReleaseVersion to, ReleaseVersion self, bool back = false)
    {
        folder.Write(SwitchHealth.Begin(state, to, back));
        var watchdog = new ProcessStartInfo(folder.ServiceOf(back ? to : self)) { UseShellExecute = false, CreateNoWindow = true };
        watchdog.ArgumentList.Add(Option);
        watchdog.ArgumentList.Add(to.ToString());
        if (_target.Argument is { } argument)
        {
            watchdog.ArgumentList.Add(argument);
        }

        (start ?? (info => Process.Start(info)?.Dispose()))(watchdog);
        say($"thermi is switching from {self} to {to}.", EventLogEntryType.Information);
    }

    /// <summary>The watchdog: 0 when the PC runs <paramref name="to"/> for good, 1 when it went back, 2 when there was nothing to do.</summary>
    public async Task<int> WatchAsync(ReleaseVersion to, string pipeName, CancellationToken stop)
    {
        using var owned = await _lock.HoldAsync(5, stop);
        if (owned is null || !folder.Trusted())
        {
            return 2;
        }

        var state = folder.Read();
        if (state.Switch is not { Running: true } pending || pending.To != to)
        {
            return 2;
        }

        var started = false;
        var timedOut = false;
        var squatted = new StrongBox<bool>();
        try
        {
            _target.Stop();
            folder.ForgetPipeHeld();
            StartOnce(pending, ref started, ref timedOut);
            if (await PollAsync(to, pipeName, squatted, stop) == Verdict.Passed)
            {
                // Pointed for good, the version has passed: what fails after this is never held against it.
                started = false;
                _target.Point(to);
                await FinishAsync(to, stop);
                return 0;
            }

            // A pipe another program held tells nothing of the version, which is tried again later rather than failed: the
            // watchdog saw its owner, or the new service could not make it and wrote so where only its owners write.
            squatted.Value |= folder.PipeHeld();
            started &= !squatted.Value;
            say(squatted.Value
                ? $"Another program held thermi's pipe while {to} started, so the PC went back to {pending.From} and tries again later."
                : $"thermi {to} served no readings in {SwitchHealth.ServicePolls} tries, so the PC went back to {pending.From}.", EventLogEntryType.Warning);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.ServiceProcess.TimeoutException)
        {
            say($"Switching to {to} failed, so the PC stays on {pending.From}: {ex.Message} {ex.InnerException?.Message}", EventLogEntryType.Warning);
        }

        GoBack(pending, Outcome(started, timedOut));
        return 1;
    }

    // A launcher a program holds open is put back at the next check; a state Windows would not let be written, tried a few
    // times, is finished at the version's next start, from the switch it still says is running.
    private async Task FinishAsync(ReleaseVersion to, CancellationToken stop)
    {
        try
        {
            folder.PlaceLaunchers(to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            say($"thermi could not put {to}'s launchers in place, and does at its next check: {ex.Message}", EventLogEntryType.Warning);
        }

        for (var tried = 1; tried <= WriteTries; tried++)
        {
            try
            {
                folder.Write(SwitchHealth.Done(folder.Read(), time.GetUtcNow()));
                shown?.Invoke(to);
                say($"thermi switched to {to}, which serves its readings.", EventLogEntryType.Information);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException && tried < WriteTries)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time, stop);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                say($"thermi runs {to}, but could not write so, and does at its next start: {ex.Message}", EventLogEntryType.Warning);
            }
        }
    }

    // Started and pointed back in one step, so the service is pointed at the new version only while it starts. Once
    // pointed, the new version ran, even if Windows then says it could not start it: it crashed as it began.
    private void StartOnce(PendingSwitch pending, ref bool started, ref bool timedOut)
    {
        _target.Point(pending.To);
        started = true;
        try
        {
            _target.Start(pending.To);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ServiceTimedOut or ShuttingDown })
        {
            // Windows gave up waiting, as under a boot's disk and antivirus, so the version may be slow, not broken: the switch
            // is cut and held, then tried once more. A start refused at shutdown is only held.
            started = false;
            timedOut = ex.InnerException is Win32Exception { NativeErrorCode: ServiceTimedOut };
            throw;
        }
        finally
        {
            _target.Point(pending.From);
        }
    }

    // A version that went back without failing stays ready but held a check's interval, or GoBack's start would switch again within a minute.
    private Func<UpdateState, UpdateState> Outcome(bool started, bool timedOut) => started
        ? SwitchHealth.Failed
        : state => SwitchHealth.HeldBack(state, timedOut, time.GetUtcNow());

    // A version that started and failed is never taken again; a switch that never started it is only dropped, to try later.
    // The watchdog stopped the old version itself, which is no crash of it.
    private void GoBack(PendingSwitch pending, Func<UpdateState, UpdateState> outcome)
    {
        try
        {
            _target.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException or Win32Exception)
        {
            say($"The service did not stop as thermi went back to {pending.From}: {ex.Message}", EventLogEntryType.Warning);
        }

        try
        {
            _target.Point(pending.From);
            folder.PlaceLaunchers(pending.From);
            var state = folder.Read();
            folder.Write(SwitchHealth.Stopped(outcome(state)));
        }
        finally
        {
            // Whatever failed above, the PC is never left without its sensors.
            StartAgain(pending);
        }
    }

    private void StartAgain(PendingSwitch pending)
    {
        try
        {
            _target.Start(pending.From);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException or Win32Exception)
        {
            say($"thermi {pending.From} did not start again after the switch: {ex.Message}", EventLogEntryType.Error);
        }
    }

    // One poll a second, counted, never timed: a PC that sleeps in between loses no tries.
    private async Task<Verdict> PollAsync(ReleaseVersion to, string pipeName, StrongBox<bool> squatted, CancellationToken stop)
    {
        var polls = 0;
        var verdict = Verdict.Waiting;
        while (verdict == Verdict.Waiting)
        {
            polls++;
            verdict = SwitchHealth.Service(polls, await ServesAsync(to, pipeName, squatted, stop));
            if (verdict == Verdict.Waiting)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time, stop);
            }
        }

        return verdict;
    }

    // A snapshot from the pipe, served by the new version's own process: the old one, restarted by Windows, proves nothing.
    private async Task<bool> ServesAsync(ReleaseVersion to, string pipeName, StrongBox<bool> squatted, CancellationToken stop)
    {
        // A second to connect, as a pipe not there yet is waited for to the end; then five to read a snapshot.
        using var poll = new CancellationTokenSource(TimeSpan.FromSeconds(1), time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(poll.Token, stop);
        try
        {
            // As SYSTEM, the watchdog lets the pipe's server only identify it, never act as it.
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(both.Token);
            poll.CancelAfter(TimeSpan.FromSeconds(5));
            var ours = Ours(pipe, to);
            squatted.Value |= ours is null;
            if (ours != true)
            {
                return false;
            }

            using var reader = new StreamReader(pipe);
            return SnapshotJson.Read(await reader.ReadLineAsync(both.Token)) is not null;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or Win32Exception && !stop.IsCancellationRequested)
        {
            return false;
        }
    }

    // True when the new version's own process serves the pipe, false when another of thermi's does, and null when a
    // program no administrator started holds it.
    private bool? Ours(NamedPipeClientStream pipe, ReleaseVersion to)
    {
        if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !_target.Owns(owner))
        {
            return null;
        }

        if (!SensorPInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
        {
            return false;
        }

        using var server = Process.GetProcessById((int)id);
        return server.MainModule?.FileName is { } exe && InstallLayout.Inside(exe, folder.VersionOf(to));
    }
}
