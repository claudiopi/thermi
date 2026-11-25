using Thermi.Core.Updates;

namespace Thermi.Core.Install;

/// <summary>
/// thermi's install folder as <c>thermi-sensors --adopt</c>, <c>--undo</c> and <c>--remove</c> read it
/// (docs/adr/0008-updates.md): the launchers at the top, a folder per version, <c>update\</c>, the MSI's <c>setup\</c> and
/// the copies scripts/install.ps1 makes; which of them an install runs from, which a removal takes, and which version a
/// service runs.
/// </summary>
public static class InstallLayout
{
    /// <summary>The folder the MSI installs a version in, beside the ones the service keeps, and which only the MSI removes.</summary>
    public const string Setup = "setup";

    // The start of the folder scripts/install.ps1 copies a build to before it adopts it.
    private const string InstallPrefix = "install-";

    public static IReadOnlyList<string> Launchers => ["thermi.exe", "thermi-sensors.exe"];

    /// <summary>Whether <paramref name="own"/> is a folder an install runs from in <paramref name="top"/>: the MSI's, the script's or a version's.</summary>
    public static bool InstallsFrom(string own, string top)
    {
        var full = Full(own);
        var name = Path.GetFileName(full);
        return (string.Equals(name, Setup, StringComparison.OrdinalIgnoreCase) || VersionOrCopy(name))
            && Path.GetDirectoryName(full) is { } parent && Same(parent, top);
    }

    /// <summary>
    /// Whether a removal takes the entry <paramref name="name"/> at the top, a folder when <paramref name="folder"/>:
    /// <c>update\</c>, the script's copies, the versions and the launchers; never the MSI's folder, which the MSI removes.
    /// </summary>
    public static bool Removes(string name, bool folder) => folder
        ? string.Equals(name, "update", StringComparison.OrdinalIgnoreCase) || VersionOrCopy(name)
        : Launchers.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The version of a service whose image path, <paramref name="imagePath"/>, runs it from a version's folder in
    /// <paramref name="top"/>; null for none, or one run from anywhere else, as a build before these folders ran.
    /// </summary>
    public static ReleaseVersion? Running(string top, string? imagePath)
    {
        var exe = imagePath?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Path.GetDirectoryName(exe) is { Length: > 0 } parent && Path.GetDirectoryName(parent) is { Length: > 0 } grand && Same(grand, top)
            ? ReleaseVersion.Parse(Path.GetFileName(parent))
            : null;
    }

    /// <summary>
    /// The version a service of <paramref name="imagePath"/> runs that an install of <paramref name="version"/> leaves
    /// running: a later one, which an update switched to; null for none.
    /// </summary>
    public static ReleaseVersion? Later(string top, string? imagePath, ReleaseVersion version) =>
        Running(top, imagePath) is { } running && running > version ? running : null;

    /// <summary>Whether two paths name the same file or folder, as Windows compares them.</summary>
    public static bool Same(string one, string other) => string.Equals(Full(one), Full(other), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> lies inside <paramref name="folder"/>, as Windows compares paths.</summary>
    public static bool Inside(string path, string folder) =>
        Path.GetFullPath(path).StartsWith(Full(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool VersionOrCopy(string name) =>
        name.StartsWith(InstallPrefix, StringComparison.OrdinalIgnoreCase) || ReleaseVersion.Parse(name) is not null;

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
