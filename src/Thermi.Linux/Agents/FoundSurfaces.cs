using Thermi.Linux.Desktop;

namespace Thermi.Linux.Agents;

/// <summary>
/// The surfaces the overlay last found on this session's display, which <c>thermi-cli</c> reads where no overlay runs to
/// ask: one that found none, as on Weston, ended at once, and the desktop cannot show it. Kept in the session's runtime
/// folder, which ends with the session, and only for the display it was found on.
/// </summary>
internal static class FoundSurfaces
{
    /// <summary>Records <paramref name="kind"/> for this session's display; nothing where the session has no runtime folder.</summary>
    public static void Write(SurfaceKind kind)
    {
        if (File() is not { } file)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, $"{kind} {Display()}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only thermi-cli's answer where no overlay runs reads it, which then says what it says without.
        }
    }

    /// <summary>What the overlay found on this display, or null where none recorded it.</summary>
    public static SurfaceKind? Read()
    {
        try
        {
            var said = File() is { } file && System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Split(' ', 2) : [];
            return said.Length == 2 && said[1] == Display() && Enum.TryParse<SurfaceKind>(said[0], out var kind) ? kind : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // GLib falls back to the cache folder where the session has no runtime folder, which outlives it: none is kept then.
    private static string? File() => Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 }
        ? Path.Combine(GLib.Functions.GetUserRuntimeDir(), "thermi", "surfaces")
        : null;

    private static string Display() =>
        $"{Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")}/{Environment.GetEnvironmentVariable("DISPLAY")}";
}
