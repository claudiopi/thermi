using System.Diagnostics;
using System.IO.Compression;
using Thermi.Core.Updates;
using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// Unpacks a checked zip into a new folder of its own under <c>update\</c>, refusing any entry that would land outside it,
/// a link, or more than a version could hold; then checks that the programs inside are the manifest's version and, in a
/// release build, that the signed catalog lists every file, and only then renames the folder to the version
/// (docs/adr/0008-updates.md). Nothing installed changes before that rename, and a refusal leaves nothing behind.
/// </summary>
internal static class Unpacker
{
    /// <summary>What a version may unpack to: a self-contained thermi is about 70 MB in some 400 files.</summary>
    public const long MaxBytes = 1L << 30;

    public const int MaxEntries = 10_000;

    /// <summary>The programs whose version must be the manifest's, each with its launcher.</summary>
    public static readonly IReadOnlyList<string> Programs = ["thermi-sensors", "thermi"];

    // A Unix link's type in the high half of a zip entry's external attributes.
    private const int LinkMode = 0xA000;

    /// <summary>
    /// Null when <paramref name="version"/> is unpacked into its folder, else why it was refused. <paramref name="catalog"/>
    /// checks a release's signed catalog over the folder, and is null in a build that needs none.
    /// </summary>
    public static string? Unpack(Stream zip, InstallFolder folder, ReleaseVersion version, Func<string, string?>? catalog)
    {
        var fresh = AdminFolder.Fresh(folder.Update, "unpack-");
        if (fresh is null)
        {
            return $"{folder.Update} is not a folder only administrators change";
        }

        try
        {
            var refused = Extract(zip, fresh) ?? Versions(fresh, version) ?? catalog?.Invoke(fresh);
            if (refused is not null)
            {
                return refused;
            }

            var target = folder.VersionOf(version);
            if (Directory.Exists(target) && !folder.SetAside(target))
            {
                return $"{target} is in use";
            }

            Directory.Move(fresh, target);
            return null;
        }
        catch (InvalidDataException ex)
        {
            return $"the zip is broken: {ex.Message}";
        }
        finally
        {
            if (Directory.Exists(fresh))
            {
                folder.SetAside(fresh);
            }
        }
    }

    private static string? Extract(Stream zip, string fresh)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries || archive.Entries.Sum(entry => entry.Length) > MaxBytes)
        {
            return "the zip holds more than a version could";
        }

        var root = Path.TrimEndingDirectorySeparator(fresh) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(fresh, entry.FullName));
            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains(':', StringComparison.Ordinal)
                || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || ((entry.ExternalAttributes >> 16) & 0xF000) == LinkMode)
            {
                return $"the zip's entry {entry.FullName} would land outside its folder";
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }

        return null;
    }

    // The manifest signs the zip; the programs inside sign what they are, so a manifest cannot relabel an older release.
    private static string? Versions(string fresh, ReleaseVersion version)
    {
        foreach (var program in Programs)
        {
            var library = Path.Combine(fresh, program + ".dll");
            var inside = File.Exists(library) ? ReleaseVersion.Parse(FileVersionInfo.GetVersionInfo(library).ProductVersion) : null;
            if (inside != version)
            {
                return $"{program}.dll is version {inside?.ToString() ?? "none"}, not {version}";
            }

            if (!File.Exists(Path.Combine(fresh, "launchers", program + ".exe")) || !File.Exists(Path.Combine(fresh, program + ".exe")))
            {
                return $"the zip has no {program}.exe or no launcher for it";
            }
        }

        return null;
    }
}
