using Microsoft.Extensions.Logging;
using Thermi.Windows.Logging;

namespace Thermi.Windows.SettingsFile;

/// <summary>Watches the settings file for changes made by any process, as the overlay and the window both do.</summary>
internal static class SettingsWatcher
{
    /// <summary>
    /// Runs <paramref name="changed"/> whenever the file changes, until the watcher is disposed. Without access to its
    /// folder it returns null, and the failure is logged; on a first run the folder is made here, since the file is made
    /// only once a read has found it missing.
    /// </summary>
    public static FileSystemWatcher? Watch(string filePath, Action changed, ILogger log)
    {
        var watcher = new FileSystemWatcher();
        try
        {
            watcher.Path = Directory.CreateDirectory(Path.GetDirectoryName(filePath)!).FullName;
            watcher.Filter = Path.GetFileName(filePath);

            // Editors save in different ways: in place, or by writing another file and renaming it over this one.
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.Error += (_, e) => Rewatch(watcher, changed, log, e.GetException());
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            watcher.Dispose();
            log.WatchFailed(ex);
            return null;
        }
    }

    // Windows drops a watcher's events when they come faster than it is read, and stops one whose folder failed, saying
    // so only here: it watches again and the file is read again, since a change may have been lost.
    private static void Rewatch(FileSystemWatcher watcher, Action changed, ILogger log, Exception error)
    {
        log.WatchFailed(error);
        try
        {
            watcher.EnableRaisingEvents = false;
            Directory.CreateDirectory(watcher.Path);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log.WatchFailed(ex);
        }

        changed();
    }
}
