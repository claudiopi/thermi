using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>How the updater tells a folder only administrators change, and makes a new one that is from the start.</summary>
internal sealed record FolderAccess(Func<string, bool> AdminsOnly, Func<string, string, string?> Fresh)
{
    public static FolderAccess Admins { get; } = new(AdminFolder.AdminsOnly, AdminFolder.Fresh);
}
