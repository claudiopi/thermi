using System.Diagnostics;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.SensorHost.Install;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.User;

/// <summary>
/// <c>thermi-sensors --user</c>: the sensors as the user, when the service is not installed
/// (docs/adr/0016-admin-on-demand.md). A hidden process of the user's that serves the pipe named for the user's SID while an
/// overlay or window reads it, ends 10 s after the last one left, so it never outlives what it serves, and in a per-user install
/// updates it as the service does.
/// </summary>
internal static class UserMode
{
    /// <summary>The exit code of a host that found one of its events already made: someone else could steer it at will.</summary>
    public const int Taken = 3;

    /// <summary>The event that ends the host, which a switch's watchdog sets.</summary>
    public const string StopEvent = "stop";

    public static readonly TimeSpan FirstClient = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the host stays once its last client left: a switch's watchdog reads the new host and leaves before the
    /// overlay comes back to it.
    /// </summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(10);

    // ADR 0008's codes, each an event of its own, since an event carries no data to parse.
    private static readonly int[] Codes = [UpdateSchedule.QuietCode, UpdateSchedule.CheckCode, UpdateSchedule.RestartCode, InstallForEveryone.Code];

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public static string PipeOf(string pipeName, SecurityIdentifier user) => pipeName + "-" + user.Value;

    /// <summary>The name of the host's event <paramref name="what"/>, in the user's session.</summary>
    public static string EventOf(string pipe, string what) => @"Local\" + pipe + "-" + what;

    public static async Task<int> RunAsync(SensorHostEnvironment environment, CancellationToken stop)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var pipe = PipeOf(environment.PipeName, user);
        var log = new FileLog(environment.UserLog, environment.Time);
        var events = Events(pipe, user);
        try
        {
            if (events.Any(each => each.Handle is null))
            {
                log.Say($"One of the {pipe} events is already made, by another host of this user's or another program, so this host does not run.",
                    EventLogEntryType.Warning);
                return Taken;
            }

            return await ServeAsync(environment, pipe, user, log, events, stop);
        }
        finally
        {
            events.ForEach(each => each.Handle?.Dispose());
        }
    }

    /// <summary>The watchdog of a per-user switch, run as the user: the same as the service's, on the user's host.</summary>
    public static async Task<int> WatchAsync(ReleaseVersion to, SensorHostEnvironment environment, CancellationToken stop)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var pipe = PipeOf(environment.PipeName, user);
        var log = new FileLog(environment.UserLog, environment.Time);
        var folder = InstallFolder.Of(AppContext.BaseDirectory, UpdateBuild.Of(typeof(UserMode).Assembly).Version, FolderAccess.ForUser(user));
        return folder is null
            ? 2
            : await new Switcher(folder, environment.Time, log.Say, shown: UserSetup.Show, target: each => new UserTarget(each, pipe, user))
                .WatchAsync(to, pipe, stop);
    }

    /// <summary>A per-user install's: its folder only the user, SYSTEM and administrators write, and no service to register.</summary>
    public static Installation Installation(string top, SensorHostEnvironment environment)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var folder = new InstallFolder(top, FolderAccess.ForUser(user));
        return new Installation(folder, new UserSetup(folder, PipeOf(environment.PipeName, user), user), environment.Time, environment.Error.WriteLine);
    }

    private static async Task<int> ServeAsync(
        SensorHostEnvironment environment, string pipe, SecurityIdentifier user, FileLog log, List<HostEvent> events, CancellationToken stop)
    {
        var time = environment.Time;
        var build = UpdateBuild.Of(typeof(UserMode).Assembly);
        var folder = InstallFolder.Of(AppContext.BaseDirectory, build.Version, FolderAccess.ForUser(user));
        using var client = new ManifestClient(time);
        using var updater = folder is null ? null : new Updater(build, folder, client, time, log.Say, target: each => new UserTarget(each, pipe, user));
        var machine = folder is null || updater is null ? null : new MachineInstaller(build, folder, client, UpdateHost.Windows, time, log, updater.Busy);
        using var source = environment.Open((device, took, error) => log.Say($"{device} took {took}: {error?.Message}", EventLogEntryType.Warning));
        using var server = new SensorPipeServer(source.ReadAsync, time, pipe, SensorPipeServer.ForUser(user))
        {
            Squatted = () => log.Say($"Another program holds the {pipe} pipe, so this host serves nothing until it closes it.", EventLogEntryType.Warning),
        };
        using var done = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Act(updater, log, updates => updates.Started());
        var waits = events.Select(each => Listen(each, new Steering(updater, machine, log), done)).ToList();
        try
        {
            var updating = updater?.RunAsync(done.Token) ?? Task.CompletedTask;
            var serving = server.RunAsync(done.Token);
            await WhileReadAsync(server, () => machine?.Running == true, time, done);
            await serving;
            await updating.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            waits.ForEach(wait => wait.Unregister(null));
        }

        // A run that ends without the mark counts as a crash, by ADR 0008's rule.
        Act(updater, log, updates => updates.Stopped());
        return 0;
    }

    private static async Task WhileReadAsync(SensorPipeServer server, Func<bool> installing, TimeProvider time, CancellationTokenSource done)
    {
        var started = time.GetTimestamp();
        var read = false;
        using var timer = new PeriodicTimer(Tick, time);
        try
        {
            while (await timer.WaitForNextTickAsync(done.Token))
            {
                if (server.Clients > 0)
                {
                    read = true;
                    started = time.GetTimestamp();
                }
                else if (time.GetElapsedTime(started) >= (read ? Linger : FirstClient) && !installing())
                {
                    await done.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ended by the stop event, the caller, or no one left to read.
        }
    }

    private static RegisteredWaitHandle Listen(HostEvent hostEvent, Steering steering, CancellationTokenSource done) =>
        ThreadPool.RegisterWaitForSingleObject(
            hostEvent.Handle!,
            (_, _) =>
            {
                if (hostEvent.Code is not { } code)
                {
                    done.Cancel();
                }
                else if (!done.IsCancellationRequested)
                {
                    _ = Task.Run(() => Tell(steering, code, done.Token), CancellationToken.None);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

    private static Task Tell(Steering steering, int code, CancellationToken stop)
    {
        if (steering.Updater is not { } updater || steering.Machine is not { } machine)
        {
            return Task.CompletedTask;
        }

        switch (code)
        {
            case InstallForEveryone.Code:
                return machine.RunAsync(MachineInstaller.MsiexecAsync, stop);
            case UpdateSchedule.CheckCode:
                return updater.AskedAsync(stop);
            case UpdateSchedule.RestartCode:
                return updater.RestartAsync(stop);
            default:
                Act(updater, steering.Log, updates => updates.Quiet());
                return Task.CompletedTask;
        }
    }

    // What the updater meets is written to the log and never ends the host.
    private static void Act(Updater? updater, FileLog log, Action<Updater> step)
    {
        try
        {
            if (updater is not null)
            {
                step(updater);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Say($"thermi's update step failed: {ex.Message}", EventLogEntryType.Warning);
        }
    }

    // The stop event and one per code, each the host's own; a handle is null when its name was already there.
#pragma warning disable CA2000 // The caller disposes every handle in its finally.
    private static List<HostEvent> Events(string pipe, SecurityIdentifier user) =>
        [
            new(null, Event(EventOf(pipe, StopEvent), user)),
            .. Codes.Select(code => new HostEvent(code, Event(EventOf(pipe, code.ToString(CultureInfo.InvariantCulture)), user))),
        ];
#pragma warning restore CA2000

    private static EventWaitHandle? Event(string name, SecurityIdentifier user)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
#pragma warning disable CA2000 // Returned to the caller, which disposes it; one already there is disposed here.
        var handle = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out var made, security);
#pragma warning restore CA2000
        if (made)
        {
            return handle;
        }

        handle.Dispose();
        return null;
    }

    /// <summary>One of the host's events: the stop, with no code, one of ADR 0008's codes, or "install for everyone".</summary>
    private sealed record HostEvent(int? Code, EventWaitHandle? Handle);

    /// <summary>What the events steer, both null outside a per-user install.</summary>
    private sealed record Steering(Updater? Updater, MachineInstaller? Machine, FileLog Log);
}
