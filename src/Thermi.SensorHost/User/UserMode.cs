using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Thermi.SensorHost.User;

/// <summary>
/// <c>thermi-sensors --user</c>: the sensors as the user, when the service is not installed
/// (docs/adr/0016-admin-on-demand.md). A hidden process of the user's that serves the pipe named for the user's SID while an
/// overlay or window reads it, and ends once the last one left, so it never outlives what it serves.
/// </summary>
internal static class UserMode
{
    /// <summary>The exit code of a host that found its stop event already made: someone else could end it at will.</summary>
    public const int Taken = 3;

    /// <summary>How long a host that never had a client waits for its first.</summary>
    public static readonly TimeSpan FirstClient = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public static string PipeOf(string pipeName, SecurityIdentifier user) => pipeName + "-" + user.Value;

    public static async Task<int> RunAsync(SensorHostEnvironment environment, CancellationToken stop)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var pipe = PipeOf(environment.PipeName, identity.User!);
        var log = new FileLog(environment.UserLog, environment.Time);
        using var ended = Event(pipe + "-stop", identity.User!);
        if (ended is null)
        {
            log.Say($"Another program made the {pipe}-stop event first, so this host does not run.", EventLogEntryType.Error);
            return Taken;
        }

        using var source = environment.Open((device, took, error) => log.Say($"{device} took {took}: {error?.Message}", EventLogEntryType.Warning));
        using var server = new SensorPipeServer(source.ReadAsync, environment.Time, pipe, SensorPipeServer.ForUser(identity.User!))
        {
            Squatted = () => log.Say($"Another program holds the {pipe} pipe, so this host serves nothing until it closes it.", EventLogEntryType.Warning),
        };
        using var done = CancellationTokenSource.CreateLinkedTokenSource(stop);
        using var stopped = When(ended, done);
        var serving = server.RunAsync(done.Token);
        await WhileReadAsync(server, environment.Time, done);
        await serving;
        return 0;
    }

    // Ends the host once no one reads it: after its last client left, or when none came within FirstClient.
    private static async Task WhileReadAsync(SensorPipeServer server, TimeProvider time, CancellationTokenSource done)
    {
        var started = time.GetTimestamp();
        var read = false;
        using var timer = new PeriodicTimer(Tick, time);
        try
        {
            while (await timer.WaitForNextTickAsync(done.Token))
            {
                read |= server.Clients > 0;
                if (server.Clients == 0 && (read || time.GetElapsedTime(started) >= FirstClient))
                {
                    await done.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ended: by the stop event, the caller, or no one left to read.
        }
    }

    // Null when the name was already there: the host makes its events itself, for its user alone.
    private static EventWaitHandle? Event(string name, SecurityIdentifier user)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
#pragma warning disable CA2000 // Returned to the caller, which disposes it; one already there is disposed here.
        var handle = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, @"Local\" + name, out var made, security);
#pragma warning restore CA2000
        if (made)
        {
            return handle;
        }

        handle.Dispose();
        return null;
    }

    private static RegisteredWait When(WaitHandle handle, CancellationTokenSource done) =>
        new(ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => done.Cancel(), null, Timeout.Infinite, executeOnlyOnce: true));

    private sealed class RegisteredWait(RegisteredWaitHandle wait) : IDisposable
    {
        public void Dispose() => wait.Unregister(null);
    }
}
