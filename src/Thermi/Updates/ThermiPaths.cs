using Thermi.Core.Updates;

namespace Thermi.Updates;

/// <summary>
/// The two executables a thermi process knows (docs/adr/0008-updates.md): its own version's, which it starts its settings
/// window from so both run one version, and the launcher at the top of the install folder, whose path never changes, which
/// the tray icons' ids, "Start with Windows" and a fresh overlay name. Outside an installed layout both are the process's own.
/// </summary>
internal static class ThermiPaths
{
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
            var own = Path.Combine(Path.GetDirectoryName(Launcher)!, version.ToString(), "launchers", "thermi.exe");
            return File.Exists(own) && File.ReadAllBytes(own).AsSpan().SequenceEqual(File.ReadAllBytes(Launcher));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
