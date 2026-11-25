using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Install;
using Thermi.Logging;
using Thermi.Overlay;
using Thermi.Updates;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Thermi.Install;

/// <summary>
/// The pipe the overlay and the window read (docs/adr/0016-admin-on-demand.md): the service's whenever it is installed,
/// else the user's own host's, trusted only when its process runs from this install at no lower integrity than this one.
/// With <paramref name="starts"/>, as the overlay's, it starts that host when none serves, never in a loop.
/// </summary>
internal sealed class SensorPipes(TimeProvider time, ILogger log, bool starts) : ISensorPipe
{
    private static readonly string User = CurrentUser();

    private static readonly int Own = OwnIntegrity();

    // The top of a per-user install, where its launchers are; null outside an installed layout, where no host is trusted.
    private static readonly string? Folder = ThermiPaths.Updates is null ? null : Path.GetDirectoryName(ThermiPaths.Launcher);

    // Whether the last pipe picked by any client of this process was the user's own host's.
    private static bool _perUser;

    private readonly Lock _lock = new();

    private string _name = UserHost.ServicePipe;
    private Process? _host;
    private long _startedAt;
    private long? _retryAt;
    private TimeSpan? _wait;

    public string Name
    {
        get
        {
            // Outside an installed layout, as a build run by hand, the pipe is the service's or a terminal's --console.
            var name = Folder is null ? UserHost.ServicePipe : UserHost.Pipe(ServiceInstalled(), User);
            Volatile.Write(ref _name, name);
            Volatile.Write(ref _perUser, name != UserHost.ServicePipe);
            return name;
        }
    }

    public bool Trusts(NamedPipeClientStream pipe)
    {
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (Volatile.Read(ref _name) == UserHost.ServicePipe)
        {
            return SensorClient.IsTrustedOwner(owner);
        }

        return owner is not null && Folder is not null && ServerOf(pipe, owner.Value) is { } server
            && UserHost.Trusts(server, User, Folder, Own);
    }

    public void Missing()
    {
        if (!starts || Folder is null || Volatile.Read(ref _name) == UserHost.ServicePipe)
        {
            return;
        }

        lock (_lock)
        {
            var now = time.GetTimestamp();
            if (_host is { } host)
            {
                if (!host.HasExited)
                {
                    // Still opening the sensors before it makes its pipe.
                    return;
                }

                host.Dispose();
                _host = null;
                _wait = UserHost.NextTry(ran: time.GetElapsedTime(_startedAt, now), _wait, Locked());
                _retryAt = _wait is { } wait ? now + (long)(wait.TotalSeconds * time.TimestampFrequency) : null;
                return;
            }

            if ((_retryAt is { } at && now < at) || Locked())
            {
                return;
            }

            Start(now);
        }
    }

    private void Start(long now)
    {
        var path = Path.Combine(Folder!, "thermi-sensors.exe");
        try
        {
            _host = Process.Start(Host());
            _startedAt = now;
            _retryAt = null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.UserHostNotStarted(path, ex.Message);
            _wait = UserHost.NextTry(TimeSpan.Zero, _wait, locked: false);
            _retryAt = now + (long)(_wait!.Value.TotalSeconds * time.TimestampFrequency);
        }
    }

    public static bool PerUser => Volatile.Read(ref _perUser);

    /// <summary>The user's own host's pipe in a per-user install, null outside one, which names its events too.</summary>
    public static string? UserPipe => Folder is null ? null : UserHost.Pipe(serviceInstalled: false, User);

    // Held by a watchdog or an install, as the host's SwitchLock tells it: a file a cut left behind holds nothing, and
    // only the host it would keep from starting finishes or undoes that switch.
    /// <summary>Starts the user's own host, as the window's "Start it" does in a per-user install, with no rights asked.</summary>
    public static bool StartUserHost()
    {
        try
        {
            using var host = Folder is null ? null : Process.Start(Host());
            return host is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The overlay tries again with its backoff, and the window keeps its bar.
            return false;
        }
    }

    private static ProcessStartInfo Host() =>
        new(Path.Combine(Folder!, "thermi-sensors.exe"), "--user") { UseShellExecute = false, CreateNoWindow = true };

    private static bool Locked()
    {
        try
        {
            using var free = new FileStream(Path.Combine(Folder!, "update", "switch.lock"), FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool ServiceInstalled()
    {
        using var service = new ServiceController(StartSensors.Service);
        try
        {
            _ = service.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    private static int OwnIntegrity()
    {
        using var self = Process.GetCurrentProcess();
        return IntegrityOf(self.SafeHandle);
    }

    // The process that made the pipe, as Windows names it; null when it is gone or hides from this user.
    private static PipeServer? ServerOf(NamedPipeClientStream pipe, string owner)
    {
        if (!PInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
        {
            return null;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        Span<char> path = stackalloc char[1024];
        var length = (uint)path.Length;
        return !process.IsInvalid && PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref length)
            ? new PipeServer(owner, new string(path[..(int)length]), IntegrityOf(process))
            : null;
    }

    // The mandatory label's last part, as 0x2000 for medium; 0 when it cannot be read, which no host passes.
    private static unsafe int IntegrityOf(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            return 0;
        }

        using (token)
        {
            Span<byte> buffer = stackalloc byte[128];
            fixed (byte* label = buffer)
            {
                if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenIntegrityLevel, label, (uint)buffer.Length, out _))
                {
                    return 0;
                }

                var sid = ((TOKEN_MANDATORY_LABEL*)label)->Label.Sid;
                return (int)*PInvoke.GetSidSubAuthority(sid, (uint)(*PInvoke.GetSidSubAuthorityCount(sid) - 1));
            }
        }
    }
}
