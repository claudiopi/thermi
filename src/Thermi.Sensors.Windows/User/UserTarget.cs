using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.Sensors.Windows.Updates;
using Windows.Win32;

namespace Thermi.Sensors.Windows.User;

/// <summary>
/// The user's own host as a switch stops and starts it (docs/adr/0016-admin-on-demand.md): nothing is pointed, since the
/// launchers move only once the new version served, so a cut leaves them on one that did.
/// </summary>
internal sealed class UserTarget(InstallFolder folder, string pipe, SecurityIdentifier user) : ISwitchTarget
{
    public const string Option = "--user";

    // As long as the service manager waits for a service to stop.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // The host this switch started, ended too when it never served, as its pipe cannot name it.
    private Process? _started;

    public string? Argument => Option;

    /// <summary>
    /// Tells the host serving the user's pipe, or the one this switch started, to stop, and waits until it is gone, so the
    /// next one can make its pipe and events; a host of this install that does not stop in time is ended.
    /// </summary>
    public void Stop()
    {
        using var serving = Serving();
        if (EventWaitHandle.TryOpenExisting(UserHost.EventOf(pipe, UserMode.StopEvent), out var stop))
        {
            using (stop)
            {
                stop.Set();
            }
        }

        foreach (var host in (Process?[])[serving, _started])
        {
            if (host is not null && !host.WaitForExit(Wait))
            {
                host.Kill();
                host.WaitForExit(Wait);
            }
        }

        _started?.Dispose();
        _started = null;
    }

    public void Point(ReleaseVersion version)
    {
        // A host started by hand: nothing Windows starts on its own points at a version.
    }

    public void Start(ReleaseVersion version)
    {
        var host = new ProcessStartInfo(folder.ServiceOf(version)) { UseShellExecute = false, CreateNoWindow = true };
        host.ArgumentList.Add(Option);
        _started?.Dispose();
        _started = Process.Start(host) ?? throw new Win32Exception($"{host.FileName} did not start.");
    }

    public bool Owns(SecurityIdentifier owner) => owner == user;

    // The host's process, from its pipe, when the user owns the pipe and the process runs from this install; null when
    // none serves it now, or another program took the name, which is never ended.
    private Process? Serving()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.None, TokenImpersonationLevel.Identification);
            client.Connect(TimeSpan.FromSeconds(1));
            if (!Owns((SecurityIdentifier)client.GetAccessControl().GetOwner(typeof(SecurityIdentifier))!)
                || !SensorPInvoke.GetNamedPipeServerProcessId(client.SafePipeHandle, out var id))
            {
                return null;
            }

            var process = Process.GetProcessById((int)id);
            if (process.MainModule?.FileName is { } file && InstallLayout.Inside(file, folder.Top))
            {
                return process;
            }

            process.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or UnauthorizedAccessException
            or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }
}
