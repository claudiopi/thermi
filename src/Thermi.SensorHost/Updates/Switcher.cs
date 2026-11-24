using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Core.Sensors;
using Thermi.Core.Updates;
using Windows.Win32;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// Moves the PC from one version to another (docs/adr/0008-updates.md). The service writes the switch down and starts a
/// watchdog, <c>thermi-sensors --switch &lt;version&gt;</c>, its own for a step forward and the older version's for a step
/// back, which outlives it: the watchdog stops the
/// service, points it at the new version, starts it and points it back at once, so a power cut leaves Windows starting the
/// old version; then it polls the pipe, and points both services at the new version for good only once it serves.
/// </summary>
internal sealed class Switcher(InstallFolder folder, TimeProvider time, Action<string, EventLogEntryType> say, Action<ProcessStartInfo>? start = null)
{
    public const string Option = "--switch";

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    // Held open by the watchdog while it switches, in update\, which only administrators change: a named mutex would be
    // one any user could make first, and so stop every switch.
    private string LockPath => Path.Combine(folder.Update, "switch.lock");

    /// <summary>Whether a watchdog is switching now, so the state is its own until it is done.</summary>
    public bool UnderWay()
    {
        try
        {
            using var free = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

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
        (start ?? (info => Process.Start(info)?.Dispose()))(watchdog);
        say($"thermi is switching from {self} to {to}.", EventLogEntryType.Information);
    }

    /// <summary>The watchdog: 0 when the PC runs <paramref name="to"/> for good, 1 when it went back, 2 when there was nothing to do.</summary>
    public async Task<int> WatchAsync(ReleaseVersion to, string pipeName, CancellationToken stop)
    {
        using var owned = await HoldAsync(stop);
        var state = folder.Read();
        if (owned is null || state.Switch is not { Running: true } pending || pending.To != to)
        {
            return 2;
        }

        var services = new ServiceConfig(folder);
        var started = false;
        var squatted = new StrongBox<bool>();
        try
        {
            ServiceConfig.Stop();
            folder.ForgetPipeHeld();
            StartOnce(services, pending, ref started);
            if (await PollAsync(to, pipeName, squatted, stop) == Verdict.Passed)
            {
                services.Point(to);
                folder.PlaceLaunchers(to);
                folder.Write(SwitchHealth.Done(folder.Read(), time.GetUtcNow()));
                say($"thermi switched to {to}, which serves its readings.", EventLogEntryType.Information);
                return 0;
            }

            // A pipe another program held tells nothing of the version, which is tried again later rather than failed: the
            // watchdog saw its owner, or the new service could not make it and wrote so where only administrators write.
            squatted.Value |= folder.PipeHeld();
            started &= !squatted.Value;
            say(squatted.Value
                ? $"Another program held thermi's pipe while {to} started, so the PC went back to {pending.From} and tries again later."
                : $"thermi {to} served no readings in {SwitchHealth.ServicePolls} tries, so the PC went back to {pending.From}.", EventLogEntryType.Warning);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException
            or System.ServiceProcess.TimeoutException)
        {
            say($"Switching to {to} failed, so the PC stays on {pending.From}: {ex.Message}", EventLogEntryType.Warning);
        }

        GoBack(services, pending, started);
        return 1;
    }

    // Started and pointed back in one step, so the service is pointed at the new version only while it starts. Once
    // pointed, the new version ran, even if Windows then says it could not start it: it crashed as it began.
    private static void StartOnce(ServiceConfig services, PendingSwitch pending, ref bool started)
    {
        services.Point(pending.To);
        started = true;
        try
        {
            ServiceConfig.Start();
        }
        finally
        {
            services.Point(pending.From);
        }
    }

    // A version that started and failed is never taken again; a switch that never started it is only dropped, to try later.
    // The watchdog stopped the old version itself, which is no crash of it.
    private void GoBack(ServiceConfig services, PendingSwitch pending, bool started)
    {
        try
        {
            ServiceConfig.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            say($"The service did not stop as thermi went back to {pending.From}: {ex.Message}", EventLogEntryType.Warning);
        }

        try
        {
            services.Point(pending.From);
            folder.PlaceLaunchers(pending.From);
            var state = folder.Read();
            folder.Write(SwitchHealth.Stopped(started ? SwitchHealth.Failed(state) : state with { Switch = null }));
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
            ServiceConfig.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            say($"thermi {pending.From} did not start again after the switch: {ex.Message}", EventLogEntryType.Error);
        }
    }

    // A service asking whether a switch is under way opens the lock for a moment, so a few tries tell it from a watchdog.
    private async Task<FileStream?> HoldAsync(CancellationToken stop)
    {
        for (var tries = 0; tries < 5; tries++)
        {
            try
            {
                return new FileInfo(LockPath)
                    .Create(FileMode.OpenOrCreate, FileSystemRights.FullControl, FileShare.None, 1, FileOptions.DeleteOnClose, AdminsOnly());
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), time, stop);
            }
        }

        return null;
    }

    // Users may read update\, and would hold a lock a power cut left behind open: none but administrators may open it.
    private static FileSecurity AdminsOnly()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
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
        using var poll = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(poll.Token, stop);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(both.Token);
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
        if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || (owner != LocalSystem && owner != Administrators))
        {
            return null;
        }

        if (!SensorPInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
        {
            return false;
        }

        using var server = Process.GetProcessById((int)id);
        var exe = server.MainModule?.FileName;
        return exe is not null && exe.StartsWith(folder.VersionOf(to) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
