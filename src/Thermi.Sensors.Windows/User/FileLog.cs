using System.Diagnostics;
using System.Globalization;

namespace Thermi.Sensors.Windows.User;

/// <summary>
/// Where the user's host says what the service writes to the event log: only an administrator can register an event
/// source, and a standard user's write to one that is missing throws.
/// </summary>
internal sealed class FileLog(string path, TimeProvider time)
{
    // Past this the log starts over, so a host that warns every few seconds never fills the disk.
    private const long Largest = 1024 * 1024;

    private readonly Lock _lock = new();

    public void Say(string message, EventLogEntryType type)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{time.GetUtcNow():u} {type}: {message}{Environment.NewLine}");
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (new FileInfo(path) is { Exists: true, Length: > Largest })
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must not end the host.
            }
        }
    }
}
