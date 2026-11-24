using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core.Sensors;
using Thermi.Core.Updates;
using Windows.Win32;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// Moves the PC from one version to another (docs/adr/0008-updates.md). The service writes the switch down and starts its
/// own executable as the watchdog, <c>thermi-sensors --switch &lt;version&gt;</c>, which outlives it: the watchdog stops the
/// service, points it at the new version, starts it and points it back at once, so a power cut leaves Windows starting the
/// old version; then it polls the pipe, and points both services at the new version for good only once it serves.
/// </summary>
internal sealed class Switcher(InstallFolder folder, TimeProvider time, Action<string, EventLogEntryType> say)
{
    /// <summary>Held by the watchdog while a switch is under way: only services and administrators may make one of the name.</summary>
    public const string MutexName = @"Global\thermi-switch";

    public const string Option = "--switch";

    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>Whether a watchdog is switching now, so the state is its own until it is done.</summary>
    public static bool UnderWay()
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Begin(UpdateState state, ReleaseVersion to, ReleaseVersion self)
    {
        folder.Write(SwitchHealth.Begin(state, to));
        var start = new ProcessStartInfo(folder.ServiceOf(self)) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Option);
        start.ArgumentList.Add(to.ToString());
        Process.Start(start)?.Dispose();
        say($"thermi is switching from {self} to {to}.", EventLogEntryType.Information);
    }

    /// <summary>The watchdog: 0 when the PC runs <paramref name="to"/> for good, 1 when it went back, 2 when there was nothing to do.</summary>
    public async Task<int> WatchAsync(ReleaseVersion to, string pipeName, CancellationToken stop)
    {
        using var owned = new Mutex(initiallyOwned: true, MutexName, out var created);
        var state = folder.Read();
        if (!created || state.Switch is not { Running: true } pending || pending.To != to)
        {
            return 2;
        }

        var services = new ServiceConfig(folder);
        var started = false;
        try
        {
            ServiceConfig.Stop();
            StartOnce(services, pending, ref started);
            if (await PollAsync(to, pipeName, stop) == Verdict.Passed)
            {
                services.Point(to);
                folder.PlaceLaunchers(to);
                folder.Write(SwitchHealth.Done(folder.Read(), time.GetUtcNow()));
                say($"thermi switched to {to}, which serves its readings.", EventLogEntryType.Information);
                return 0;
            }

            say($"thermi {to} served no readings in {SwitchHealth.ServicePolls} tries, so the PC went back to {pending.From}.", EventLogEntryType.Warning);
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

        services.Point(pending.From);
        folder.PlaceLaunchers(pending.From);
        var state = folder.Read();
        folder.Write(SwitchHealth.Stopped(started ? SwitchHealth.Failed(state) : state with { Switch = null }));
        ServiceConfig.Start();
    }

    // One poll a second, counted, never timed: a PC that sleeps in between loses no tries.
    private async Task<Verdict> PollAsync(ReleaseVersion to, string pipeName, CancellationToken stop)
    {
        var polls = 0;
        var verdict = Verdict.Waiting;
        while (verdict == Verdict.Waiting)
        {
            polls++;
            verdict = SwitchHealth.Service(polls, await ServesAsync(to, pipeName, stop));
            if (verdict == Verdict.Waiting)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), time, stop);
            }
        }

        return verdict;
    }

    // A snapshot from the pipe, served by the new version's own process: the old one, restarted by Windows, proves nothing.
    private async Task<bool> ServesAsync(ReleaseVersion to, string pipeName, CancellationToken stop)
    {
        using var poll = new CancellationTokenSource(TimeSpan.FromSeconds(5), time);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(poll.Token, stop);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(both.Token);
            if (!Ours(pipe, to))
            {
                return false;
            }

            using var reader = new StreamReader(pipe);
            return SnapshotJson.Read(await reader.ReadLineAsync(both.Token)) is not null;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException && !stop.IsCancellationRequested)
        {
            return false;
        }
    }

    private bool Ours(NamedPipeClientStream pipe, ReleaseVersion to)
    {
        if (pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || (owner != LocalSystem && owner != Administrators)
            || !SensorPInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
        {
            return false;
        }

        using var server = Process.GetProcessById((int)id);
        var exe = server.MainModule?.FileName;
        return exe is not null && exe.StartsWith(folder.VersionOf(to) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
