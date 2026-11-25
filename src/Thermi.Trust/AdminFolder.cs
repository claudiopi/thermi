using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;

namespace Thermi.Trust;

/// <summary>
/// Folders only administrators, SYSTEM and Windows' installer may change, as thermi's own under Program Files, where a
/// process running as SYSTEM may keep what it runs. A folder a user could make first, as one in ProgramData, would let
/// them swap a link in between a check and a change made by its path, or leave a DLL the next program loads.
/// </summary>
public static class AdminFolder
{
    private const FileSystemRights Writes = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Installer = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>
    /// A new folder in <paramref name="parent"/>, named <paramref name="prefix"/> and a part nobody can guess, made with
    /// administrators' access from the start, and <paramref name="user"/>'s for a per-user install, failing rather than
    /// reusing one that is there: nobody else ever held it. Null when <paramref name="parent"/> is not one only they change;
    /// the parent is only read.
    /// </summary>
    public static string? Fresh(string parent, string prefix, SecurityIdentifier? user = null)
    {
        var fresh = Path.Combine(parent, prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        return AdminsOnly(parent, user) && Create(fresh, user) ? fresh : null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a real folder, owned by administrators, that nobody else may write to; with
    /// <paramref name="user"/>, a per-user install's, which that user may own and write too, since anyone who can write it
    /// already runs as them (docs/adr/0016-admin-on-demand.md). A rule only its children inherit is no access to it. Read
    /// only, and never throws.
    /// </summary>
    public static bool AdminsOnly(string path, SecurityIdentifier? user = null)
    {
        try
        {
            var folder = new DirectoryInfo(path);
            if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            var now = folder.GetAccessControl();
            return Trusted(now.GetOwner(typeof(SecurityIdentifier)), user)
                && now.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .All(rule => rule.AccessControlType == AccessControlType.Deny
                        || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)
                        || ((int)rule.FileSystemRights & (int)Writes) == 0
                        || Trusted(rule.IdentityReference, user));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static unsafe bool Create(string path, SecurityIdentifier? user)
    {
        fixed (byte* descriptor = Protected(user).GetSecurityDescriptorBinaryForm())
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = descriptor };
            return PInvoke.CreateDirectory(path, attributes);
        }
    }

    /// <summary>Whether <paramref name="who"/> is SYSTEM or the administrators, as owns what a service or an administrator made.</summary>
    public static bool IsAdministrator(IdentityReference? who) => who is SecurityIdentifier sid && (sid == System || sid == Administrators);

    /// <summary>
    /// A file's access for SYSTEM and administrators alone, and <paramref name="user"/> in a per-user install, which no other
    /// user can open, even to read, and so hold open.
    /// </summary>
    public static FileSecurity PrivateFile(SecurityIdentifier? user = null)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        if (user is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    // Program Files' own owner and writer is TrustedInstaller, which only Windows' servicing runs as.
    private static bool Trusted(IdentityReference? who, SecurityIdentifier? user) =>
        who is SecurityIdentifier sid && (sid == System || sid == Administrators || sid == Installer || sid == user);

    private static DirectorySecurity Protected(SecurityIdentifier? user)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags All = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(
            new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(user is null
            ? new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, All, PropagationFlags.None, AccessControlType.Allow)
            : new FileSystemAccessRule(user, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
