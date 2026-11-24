using System.Security.AccessControl;
using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// How the updater tells a folder only administrators change, makes a new one that is from the start, and makes a file
/// no user may open; a test's, which runs without admin rights, makes its files as the folder's.
/// </summary>
internal sealed record FolderAccess(Func<string, bool> AdminsOnly, Func<string, string, string?> Fresh, Func<FileSecurity?> Private)
{
    public static FolderAccess Admins { get; } = new(AdminFolder.AdminsOnly, AdminFolder.Fresh, AdminFolder.PrivateFile);
}
