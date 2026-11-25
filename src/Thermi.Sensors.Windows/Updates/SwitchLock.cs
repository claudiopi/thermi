using System.Security.AccessControl;
using Thermi.Trust;

namespace Thermi.Sensors.Windows.Updates;

/// <summary>
/// The lock a watchdog holds while it switches versions, and an install while it changes the services: a file in
/// <c>update\</c>, which only its owners change, administrators or a per-user install's user, since a named mutex would be one any user could make first and so
/// stop every switch.
/// </summary>
internal sealed class SwitchLock(InstallFolder folder, TimeProvider time)
{
    private string LockPath => Path.Combine(folder.Update, "switch.lock");

    /// <summary>Whether a watchdog or an install holds the lock now.</summary>
    public bool Held() => LockFile.Held(LockPath);

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
                    .Create(FileMode.OpenOrCreate, FileSystemRights.FullControl, FileShare.None, 1, FileOptions.DeleteOnClose, folder.Private());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held, or made by an administrator for a process that is not one, as a test's.
                await Task.Delay(TimeSpan.FromMilliseconds(200), time, stop);
            }
        }

        return null;
    }
}
