using System.Security.AccessControl;
using System.Security.Principal;
using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// How the updater tells a folder only administrators change, makes a new one that is from the start, and makes a file
/// no user may open; a test's, which runs without admin rights, makes its files as the folder's.
/// </summary>
internal sealed record FolderAccess(Func<string, bool> AdminsOnly, Func<string, string, string?> Fresh, Func<FileSecurity?> Private)
{
    public static FolderAccess Admins { get; } =
        new(path => AdminFolder.AdminsOnly(path), (parent, prefix) => AdminFolder.Fresh(parent, prefix), () => AdminFolder.PrivateFile());

    /// <summary>A per-user install's: only <paramref name="user"/>, SYSTEM and administrators write it (docs/adr/0016-admin-on-demand.md).</summary>
    public static FolderAccess ForUser(SecurityIdentifier user) =>
        new(path => AdminFolder.AdminsOnly(path, user), (parent, prefix) => AdminFolder.Fresh(parent, prefix, user), () => AdminFolder.PrivateFile(user));
}
