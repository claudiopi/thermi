using System.Security.AccessControl;
using System.Security.Principal;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// The lock a watchdog holds while it switches versions, and an install while it changes the services: a file in
/// <c>update\</c>, which only administrators change, since a named mutex would be one any user could make first and so
/// stop every switch.
/// </summary>
internal sealed class SwitchLock(InstallFolder folder, TimeProvider time)
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private string LockPath => Path.Combine(folder.Update, "switch.lock");

    /// <summary>Whether a watchdog or an install holds the lock now.</summary>
    public bool Held()
    {
        try
        {
            using var free = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.None);
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

    /// <summary>
    /// The lock, tried every 200 ms <paramref name="tries"/> times, or null. A service asking whether a switch is under way
    /// opens it for a moment, so a few tries tell that from a holder.
    /// </summary>
    public async Task<FileStream?> HoldAsync(int tries, CancellationToken stop)
    {
        Directory.CreateDirectory(folder.Update);
        for (var tried = 0; tried < tries; tried++)
        {
            try
            {
                return new FileInfo(LockPath)
                    .Create(FileMode.OpenOrCreate, FileSystemRights.FullControl, FileShare.None, 1, FileOptions.DeleteOnClose, AdminsOnly());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held, or made by an administrator for a process that is not one, as a test's.
                await Task.Delay(TimeSpan.FromMilliseconds(200), time, stop);
            }
        }

        return null;
    }

    // Users may read update\, and would hold a lock a power cut left behind open: none but administrators may open it.
    private static FileSecurity AdminsOnly()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
