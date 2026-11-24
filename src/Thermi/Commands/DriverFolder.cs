using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace Thermi.Commands;

/// <summary>
/// <c>%ProgramData%\thermi\driver</c>, where the driver's installer waits while it is checked and run: made, or taken
/// back, owned by administrators and writable only by them and SYSTEM, and checked for its owner, its access and links
/// before each use.
/// </summary>
internal static class DriverFolder
{
    private const FileSystemRights Writes = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>The folder, ready; null when it is a link, or when its owner kept administrators from taking it back.</summary>
    public static string? Prepare() => Prepare(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "thermi"));

    /// <summary>The <c>driver</c> folder in <paramref name="thermi"/>, both made or kept safe, or null.</summary>
    internal static string? Prepare(string thermi)
    {
        var driver = Path.Combine(thermi, "driver");
        return Safe(new DirectoryInfo(thermi)) && Safe(new DirectoryInfo(driver)) ? driver : null;
    }

    private static bool Safe(DirectoryInfo folder)
    {
        try
        {
            if (!folder.Exists)
            {
                folder.Create(AdminsOnly());
                folder.Refresh();
            }

            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            // Taken back from whoever made it: a user who made it first would otherwise block the install for good, and an
            // administrator's may have taken ProgramData's access, which lets every user add files. What it held stays
            // harmless, since the installer is checked by its hash while it is held open.
            var safe = AdminsOnly();
            safe.SetOwner(Administrators);
            folder.SetAccessControl(safe);
            var now = folder.GetAccessControl();
            return Trusted(now.GetOwner(typeof(SecurityIdentifier)))
                && now.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                    .All(rule => rule.AccessControlType == AccessControlType.Deny || ((int)rule.FileSystemRights & (int)Writes) == 0 || Trusted(rule.IdentityReference));
        }
        // Windows refuses an owner the process may not set as InvalidOperationException.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PrivilegeNotHeldException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Trusted(IdentityReference? who) => who is SecurityIdentifier sid && (sid == System || sid == Administrators);

    private static DirectorySecurity AdminsOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags All = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, All, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}

/// <summary>The download over HTTPS, read no further than the pinned size, since a longer file is not the pinned one.</summary>
internal sealed class HttpDownload : IDriverDownload
{
    public async Task<bool> DownloadAsync(Uri url, Stream into, long size, CancellationToken cancel)
    {
        // The caller's token bounds the download on its clock, so the client's own timeout is off.
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                var buffer = new byte[81_920];
                long total = 0;
                for (int read; (read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0;)
                {
                    total += read;
                    if (total > size)
                    {
                        return false;
                    }

                    await into.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

/// <summary>The installer as Windows checks and runs it: its Authenticode signature and its process, with no window.</summary>
internal sealed class WindowsInstaller : IDriverInstaller
{
    // By its path: the open file shares neither writing nor deleting, so nothing can change or replace it meanwhile.
    public unsafe bool Signed(FileStream file)
    {
        fixed (char* path = file.Name)
        {
            var info = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = path,
            };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
            };
            data.Anonymous.pFile = &info;
            var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var verdict = PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
            PInvoke.WinVerifyTrust(HWND.Null, ref action, &data);
            return verdict == 0;
        }
    }

    public async Task<int?> RunAsync(string path, IReadOnlyList<string> arguments, TimeSpan wait, TimeProvider time)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Started(start);
        if (process is null)
        {
            return null;
        }

        using var cancel = new CancellationTokenSource(wait, time);
        try
        {
            await process.WaitForExitAsync(cancel.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static Process? Started(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
