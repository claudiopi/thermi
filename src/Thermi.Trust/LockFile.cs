namespace Thermi.Trust;

/// <summary>
/// An update's <c>switch.lock</c>, as both the host and the overlay tell it: held while a process has it open, which a
/// watchdog or an install does with no sharing; a file a cut left behind holds nothing.
/// </summary>
public static class LockFile
{
    public static bool Held(string path)
    {
        try
        {
            using var free = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
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
}
