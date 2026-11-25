using System.Diagnostics;
using System.IO.Compression;
using Thermi.Core.Updates;

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

    /// <summary>
    /// Null when <paramref name="version"/> is unpacked into its folder, else why it was refused. <paramref name="catalog"/>
    /// checks a release's signed catalog over the folder, and is null in a build that needs none.
    /// </summary>
    public static Refusal? Unpack(Stream zip, InstallFolder folder, ReleaseVersion version, Func<string, string?>? catalog)
    {
        var fresh = folder.Fresh("unpack-");
        if (fresh is null)
        {
            return new($"{folder.Update} is not a folder only its owners change", ForTheZip: false);
        }

        try
        {
            if ((Extract(zip, fresh) ?? Versions(fresh, version) ?? catalog?.Invoke(fresh)) is { } refused)
            {
                return new(refused, ForTheZip: true);
            }

            return folder.Place(fresh, version) ? null : new($"{folder.VersionOf(version)} is in use", ForTheZip: false);
        }
        catch (InvalidDataException ex)
        {
            return new($"the zip is broken: {ex.Message}", ForTheZip: true);
        }
        finally
        {
            if (Directory.Exists(fresh))
            {
                folder.SetAside(fresh);
            }
        }
    }

    /// <summary>Why a zip was refused, and whether for what it holds, which fetching it again cannot change.</summary>
    public sealed record Refusal(string Why, bool ForTheZip);

    private static string? Extract(Stream zip, string fresh)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries || archive.Entries.Sum(entry => entry.Length) > MaxBytes)
        {
            return "the zip holds more than a version could";
        }

        var left = MaxBytes;
        foreach (var entry in archive.Entries)
        {
            if (!ZipEntries.LandsInside(fresh, entry.FullName, entry.ExternalAttributes))
            {
                return $"the zip's entry {entry.FullName} would land outside its folder";
            }

            var target = Path.GetFullPath(Path.Combine(fresh, entry.FullName));

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var written = Write(entry, target, left);
            if (written < 0)
            {
                return "the zip holds more than a version could";
            }

            left -= written;
        }

        return null;
    }

    // Counted as written, not as the zip declares, and flushed to the disk, so a power cut after a switch finds every file
    // whole. Less than zero once past what is left.
    private static long Write(ZipArchiveEntry entry, string target, long left)
    {
        using var source = entry.Open();
        using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920);
        var buffer = new byte[81920];
        long written = 0;
        int got;
        while ((got = source.Read(buffer)) > 0)
        {
            written += got;
            if (written > left)
            {
                return -1;
            }

            file.Write(buffer, 0, got);
        }

        file.Flush(flushToDisk: true);
        return written;
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
