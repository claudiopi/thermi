using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;
using Windows.Win32;

namespace Thermi.SensorHost.User;

/// <summary>
/// The user's own host as a switch stops and starts it (docs/adr/0016-admin-on-demand.md): nothing is pointed, since the
/// launchers move only once the new version served, so a cut leaves them on one that did.
/// </summary>
internal sealed class UserTarget(InstallFolder folder, string pipe, SecurityIdentifier user) : ISwitchTarget
{
    public const string Option = "--user";

    // As long as the service manager waits for a service to stop.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    public string? Argument => Option;

    /// <summary>Tells the host serving the user's pipe to stop, and waits until it is gone, so the new one can make its pipe and events.</summary>
    public void Stop()
    {
        using var host = Serving();
        if (EventWaitHandle.TryOpenExisting(UserHost.EventOf(pipe, UserMode.StopEvent), out var stop))
        {
            using (stop)
            {
                stop.Set();
            }
        }

        if (host is not null && !host.WaitForExit(Wait))
        {
            host.Kill();
        }
    }

    public void Point(ReleaseVersion version)
    {
        // A host started by hand: nothing Windows starts on its own points at a version.
    }

    public void Start(ReleaseVersion version)
    {
        var host = new ProcessStartInfo(folder.ServiceOf(version)) { UseShellExecute = false, CreateNoWindow = true };
        host.ArgumentList.Add(Option);
        using var started = Process.Start(host) ?? throw new Win32Exception($"{host.FileName} did not start.");
    }

    public bool Owns(SecurityIdentifier owner) => owner == user;

    // The host's process, from its pipe; null when none serves it now.
    private Process? Serving()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.None, TokenImpersonationLevel.Identification);
            client.Connect(TimeSpan.FromSeconds(1));
            return SensorPInvoke.GetNamedPipeServerProcessId(client.SafePipeHandle, out var id) ? Process.GetProcessById((int)id) : null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
