namespace Thermi.Windows.Win32;

/// <summary>Whether a named mutex exists: one this user may not open exists all the same.</summary>
internal static class NamedMutex
{
    public static bool Exists(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
