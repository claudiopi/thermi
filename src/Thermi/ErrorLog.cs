namespace Thermi;

/// <summary>
/// Where the overlay notes what went wrong instead of dying: an exception inside the window procedure would
/// end the process. Kept under a megabyte; writing to it never throws.
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1024 * 1024;

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "thermi", "thermi.log");

    public static void Write(Exception error, TimeProvider time)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
            {
                File.Delete(FilePath);
            }

            File.AppendAllText(FilePath, $"{time.GetUtcNow():O} {error}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it, and the overlay matters more than its log.
        }
    }
}
