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
    /// administrators' access from the start and failing rather than reusing one that is there: nobody else ever held it.
    /// Null when <paramref name="parent"/> is not one only administrators change; the parent is only read.
    /// </summary>
    public static string? Fresh(string parent, string prefix)
    {
        var fresh = Path.Combine(parent, prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        return AdminsOnly(parent) && Create(fresh) ? fresh : null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a real folder, owned by administrators, that nobody else may write to. A rule
    /// only its children inherit is no access to it. Read only, and never throws.
    /// </summary>
    public static bool AdminsOnly(string path)
    {
        try
        {
            var folder = new DirectoryInfo(path);
            if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            var now = folder.GetAccessControl();
            return Trusted(now.GetOwner(typeof(SecurityIdentifier)))
                && now.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .All(rule => rule.AccessControlType == AccessControlType.Deny
                        || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)
                        || ((int)rule.FileSystemRights & (int)Writes) == 0
                        || Trusted(rule.IdentityReference));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static unsafe bool Create(string path)
    {
        fixed (byte* descriptor = Protected().GetSecurityDescriptorBinaryForm())
        {
            var attributes = new SECURITY_ATTRIBUTES { nLength = (uint)sizeof(SECURITY_ATTRIBUTES), lpSecurityDescriptor = descriptor };
            return PInvoke.CreateDirectory(path, attributes);
        }
    }

    // Program Files' own owner and writer is TrustedInstaller, which only Windows' servicing runs as.
    private static bool Trusted(IdentityReference? who) =>
        who is SecurityIdentifier sid && (sid == System || sid == Administrators || sid == Installer);

    private static DirectorySecurity Protected()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags All = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(
            new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, All, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, All, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
