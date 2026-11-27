using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Windows.Updates;

/// <summary>
/// The two executables a thermi process knows (docs/adr/0008-updates.md): its own version's, which it starts its settings
/// window from so both run one version, and the launcher at the top of the install folder, whose path never changes, which
/// the tray icons' ids, "Start with Windows" and a fresh overlay name. Outside an installed layout both are the process's own.
/// </summary>
internal static class ThermiPaths
{
    private static ((string, DateTime, DateTime) Seen, bool Same)? _compared;

    public static string Own { get; } = Path.Combine(AppContext.BaseDirectory, "thermi.exe");

    public static string Launcher { get; } = LauncherOf(Own);

    /// <summary>The service's <c>update\</c> folder, beside the launcher, with the copies of its state; null outside an installed layout.</summary>
    public static string? Updates { get; } =
        string.Equals(Launcher, Own, StringComparison.OrdinalIgnoreCase) ? null : Path.Combine(Path.GetDirectoryName(Launcher)!, "update");

    public static string? State() => Updates is null || !Directory.Exists(Updates)
        ? null
        : Directory.EnumerateFiles(Updates, UpdateState.SharedPrefix + "*.json").MaxBy(copy => UpdateState.SharedNumber(Path.GetFileName(copy)));

    /// <summary>Whether the launcher at the top is the copy the version keeps under <c>launchers\</c>.</summary>
    public static bool LauncherStarts(ReleaseVersion version)
    {
        try
        {
            var top = new FileInfo(Launcher);
            var own = new FileInfo(Path.Combine(Path.GetDirectoryName(Launcher)!, version.ToString(), "launchers", "thermi.exe"));
            if (!own.Exists || !top.Exists || own.Length != top.Length)
            {
                return false;
            }

            var seen = (own.FullName, own.LastWriteTimeUtc, top.LastWriteTimeUtc);
            if (_compared is not { } compared || compared.Seen != seen)
            {
                compared = (seen, Read(own).AsSpan().SequenceEqual(Read(top)));
                _compared = compared;
            }

            return compared.Same;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Shared for delete too, so a read never holds a launcher against the service setting it aside.
    private static byte[] Read(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Whether this process runs the version the launcher starts, as always outside an installed layout; not in one an update
    /// left running on the version before, as an agent's <c>thermi-cli mcp</c>, which a newer settings file is no rollback to.
    /// </summary>
    public static bool RunsInstalled() => Updates is null || (ReleaseVersion.Parse(BuildVersion.Of(typeof(ThermiPaths).Assembly)) is { } own && LauncherStarts(own));

    /// <summary>Whether <paramref name="path"/> is one of thermi's: an overlay and its window may run from either.</summary>
    public static bool IsThermi(string path) =>
        string.Equals(path, Own, StringComparison.OrdinalIgnoreCase) || string.Equals(path, Launcher, StringComparison.OrdinalIgnoreCase);

    private static string LauncherOf(string own)
    {
        var version = Path.GetDirectoryName(own);
        var top = Path.GetDirectoryName(version);
        var launcher = top is null ? null : Path.Combine(top, Path.GetFileName(own));
        return launcher is not null && ReleaseVersion.Parse(Path.GetFileName(version)) is not null && File.Exists(launcher) ? launcher : own;
    }
}
