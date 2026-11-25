using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Install;
using Thermi.Logging;
using Thermi.Overlay;
using Thermi.Trust;
using Thermi.Updates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Security.Authorization;
using Windows.Win32.System.Threading;

namespace Thermi.Install;

/// <summary>
/// The pipe the overlay and the window read (docs/adr/0016-admin-on-demand.md): the service's whenever it is installed,
/// else the user's own host's, trusted only when its process runs from this install at no lower integrity than this one,
/// and the pipe's own label is no lower either.
/// With <paramref name="starts"/>, as the overlay's, it starts that host when none serves, never in a loop.
/// </summary>
internal sealed class SensorPipes(TimeProvider time, ILogger log, bool starts) : ISensorPipe
{
    private static readonly string User = CurrentUser();

    private static readonly int Own = OwnIntegrity();

    // SYSTEM_MANDATORY_LABEL_ACE_TYPE, which .NET reads as a custom entry.
    private const int MandatoryLabel = 0x11;

    // The mask, then a SID of one part: its revision, count and authority, and the part.
    private const int LabelLength = 16;

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

    /// <summary>
    /// Whether the fix installs thermi for everyone: in a per-user install, while it reads the user's own host; once the
    /// service serves, its window's fix is the machine's again.
    /// </summary>
    public static bool InstallsForEveryone => PerUserInstall && PerUser;

    private static bool PerUserInstall { get; } =
        Folder is not null && UserHost.PerUserInstall(Folder, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>The user's own host's pipe in a per-user install, null outside one, which names its events too.</summary>
    public static string? UserPipe => Folder is null ? null : UserHost.Pipe(serviceInstalled: false, User);

    /// <summary>
    /// Starts the user's own host, as the window's and the tray's "Start sensors" do in a per-user install, with no rights
    /// asked; never under a switch's lock, whose watchdog starts the new version's host itself.
    /// </summary>
    public static bool StartUserHost()
    {
        try
        {
            using var host = Folder is null || Locked() ? null : Process.Start(Host());
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

    // A file a cut left behind holds nothing: only the host it would keep from starting finishes or undoes that switch.
    private static bool Locked() => LockFile.Held(Path.Combine(Folder!, "update", "switch.lock"));

    public static bool ServiceInstalled() => new WindowsServices(StartSensors.Service).State() != ServiceRun.Missing;

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
    internal static PipeServer? ServerOf(NamedPipeClientStream pipe, string owner)
    {
        if (!PInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
        {
            return null;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        Span<char> path = stackalloc char[1024];
        var length = (uint)path.Length;
        return !process.IsInvalid && PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref length)
            ? new PipeServer(owner, new string(path[..(int)length]), IntegrityOf(process), LabelOf(pipe.SafePipeHandle))
            : null;
    }

    // The pipe's mandatory label's level, medium when it has none, as Windows reads an object with no label; 0 when it
    // cannot be read, which no host passes.
    private static unsafe int LabelOf(SafeHandle pipe)
    {
        if (PInvoke.GetSecurityInfo(pipe, SE_OBJECT_TYPE.SE_KERNEL_OBJECT, OBJECT_SECURITY_INFORMATION.LABEL_SECURITY_INFORMATION,
                out _, out _, out _, out _, out var descriptor) != WIN32_ERROR.ERROR_SUCCESS)
        {
            return 0;
        }

        try
        {
            var raw = new RawSecurityDescriptor(
                new ReadOnlySpan<byte>(descriptor.Value, (int)PInvoke.GetSecurityDescriptorLength(descriptor)).ToArray(), 0);
            var label = raw.SystemAcl?.OfType<CustomAce>().FirstOrDefault(ace => (int)ace.AceType == MandatoryLabel);

            // Past its mask, a label's SID has one part, the level, in its last four bytes; any other shape is no label to trust.
            return label?.GetOpaque() is not { } opaque ? UserHost.Medium
                : opaque.Length == LabelLength ? (int)BinaryPrimitives.ReadUInt32LittleEndian(opaque.AsSpan(opaque.Length - sizeof(uint)))
                : 0;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)descriptor.Value);
        }
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
