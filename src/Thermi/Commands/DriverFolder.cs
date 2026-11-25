using Thermi.Trust;

namespace Thermi.Commands;

/// <summary>
/// Where the driver's installer waits while it is checked and run: a folder of its own, new for each install, made by
/// the elevated mode in thermi's own folder under Program Files (<see cref="AdminFolder"/>).
/// </summary>
internal static class DriverFolder
{
    /// <summary>A new, empty folder for one install, or null when thermi's folder is not one only administrators change.</summary>
    public static string? Prepare() =>
        Prepare(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "thermi"));

    /// <summary>A new folder in <paramref name="parent"/>, which is only read, never changed; null when it is unsafe.</summary>
    internal static string? Prepare(string parent) => AdminFolder.Fresh(parent, "driver-");
}
