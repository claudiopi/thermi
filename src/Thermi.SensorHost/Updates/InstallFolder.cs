using System.Security.AccessControl;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// thermi's folder as updates keep it (docs/adr/0008-updates.md): the launchers at the top, a folder per kept version,
/// and <c>update\</c> with the service's state, its downloads and what it set aside to delete. Everything the updater
/// changes is here, and it checks the folder is one only administrators change before each use.
/// </summary>
internal sealed class InstallFolder(string top, FolderAccess? access = null)
{
    private readonly FolderAccess _access = access ?? FolderAccess.Admins;

    public string Top => top;

    public string Update => Path.Combine(top, "update");

    private string StatePath => Path.Combine(Update, UpdateState.FileName);

    private string Trash => Path.Combine(Update, "trash");

    // Written by a service that could not make its pipe because another program held the name, where no user writes.
    private string PipeHeldPath => Path.Combine(Update, "pipe-held");

    /// <summary>
    /// The layout a process in <paramref name="baseDirectory"/> runs in: a folder named after its version, which the
    /// launchers beside it start, even when one is missing, since the service puts them back. Null outside one.
    /// </summary>
    public static InstallFolder? Of(string baseDirectory, ReleaseVersion self)
    {
        var own = Path.TrimEndingDirectorySeparator(baseDirectory);
        var top = Path.GetDirectoryName(own);
        return top is not null && ReleaseVersion.Parse(Path.GetFileName(own)) == self ? new InstallFolder(top) : null;
    }

    public string VersionOf(ReleaseVersion version) => Path.Combine(top, version.ToString());

    public string ServiceOf(ReleaseVersion version) => Path.Combine(VersionOf(version), "thermi-sensors.exe");

    /// <summary>Whether the folder and <c>update\</c> are real folders only administrators, SYSTEM and Windows' installer change.</summary>
    public bool Trusted()
    {
        if (!Directory.Exists(Update))
        {
            // Inherits the top's access, which is checked first.
            return AdminsOnly(top) && Directory.CreateDirectory(Update) is not null && AdminsOnly(Update);
        }

        return AdminsOnly(top) && AdminsOnly(Update);
    }

    /// <summary>Whether <paramref name="path"/> is a real folder only administrators, SYSTEM and Windows' installer change.</summary>
    public bool AdminsOnly(string path) => _access.AdminsOnly(path);

    /// <summary>A new folder in <c>update\</c> named <paramref name="prefix"/> and a part nobody can guess; null when <c>update\</c> is not trusted.</summary>
    public string? Fresh(string prefix) => _access.Fresh(Update, prefix);

    /// <summary>The access of a file in <c>update\</c> no user may open; null for the folder's own, in a test.</summary>
    public FileSecurity? Private() => _access.Private();

    /// <summary>Notes that another program held the pipe's name as this service started; never throws.</summary>
    public void MarkPipeHeld()
    {
        try
        {
            using var _ = new FileInfo(PipeHeldPath)
                .Create(FileMode.Create, FileSystemRights.FullControl, FileShare.None, 1, FileOptions.None, _access.Private());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Outside an install folder only administrators change, or with update\ gone: no switch waits on it.
        }
    }

    public bool PipeHeld() => File.Exists(PipeHeldPath);

    public void ForgetPipeHeld() => File.Delete(PipeHeldPath);

    public UpdateState Read() => UpdateState.Read(StateText());

    /// <summary>The state file as it is, or null when there is none.</summary>
    public string? StateText() => File.Exists(StatePath) ? File.ReadAllText(StatePath) : null;

    /// <summary>Puts the state file back as <see cref="StateText"/> read it, or with null removes it.</summary>
    public void RestoreState(string? text)
    {
        if (text is not null)
        {
            WriteText(text);
            return;
        }

        File.Delete(StatePath);
        Share(null);
    }

    /// <summary>Writes the state whole or not at all, then the copy users read.</summary>
    public void Write(UpdateState state) => WriteText(state.Write());

    private void WriteText(string text)
    {
        Whole(StatePath, text, _access.Private());
        Share(text);
    }

    private static void Whole(string path, string text, FileSecurity? security)
    {
        var next = path + ".new";
        using (var file = new FileInfo(next).Create(FileMode.Create, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, security))
        using (var writer = new StreamWriter(file))
        {
            writer.Write(text);
            writer.Flush();
            file.Flush(flushToDisk: true);
        }

        File.Move(next, path, overwrite: true);
    }

    // The users' copy, a new file as the folder's access lets them read it, then the older ones deleted: one a user holds
    // open stays until a later write.
    private void Share(string? text)
    {
        // Every copy and a write cut short, its .new, goes once the next is in place.
        var older = Directory.EnumerateFiles(Update, UpdateState.SharedPrefix + "*").ToList();
        var next = (older.Max(path => UpdateState.SharedNumber(Path.GetFileName(path))) ?? 0) + 1;
        try
        {
            if (text is not null)
            {
                Whole(Path.Combine(Update, $"{UpdateState.SharedPrefix}{next}.json"), text, security: null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var copy in older)
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    /// <summary>
    /// Deletes every version's folder but <paramref name="keep"/>. Each is renamed aside first, which Windows refuses while
    /// any process runs from it, so a folder in use stays until a later prune. What it set aside before goes too.
    /// </summary>
    public IReadOnlyList<ReleaseVersion> Prune(IReadOnlySet<ReleaseVersion> keep)
    {
        var removed = new List<ReleaseVersion>();
        foreach (var folder in Directory.EnumerateDirectories(top))
        {
            if (ReleaseVersion.Parse(Path.GetFileName(folder)) is { } version && !keep.Contains(version) && SetAside(folder))
            {
                removed.Add(version);
            }
        }

        // What a download or an unpack left when the PC lost power or the service was ended midway.
        foreach (var left in Directory.EnumerateFileSystemEntries(Update, "unpack-*").Concat(Directory.EnumerateFiles(Update, "download-*.zip")))
        {
            SetAside(left);
        }

        EmptyTrash();
        return removed;
    }

    /// <summary>Removes <paramref name="path"/>, a file or a folder, by setting it aside first; false when it is in use.</summary>
    public bool SetAside(string path)
    {
        Directory.CreateDirectory(Trash);
        try
        {
            var aside = Path.Combine(Trash, Guid.NewGuid().ToString("N"));
            if (Directory.Exists(path))
            {
                Directory.Move(path, aside);
            }
            else if (File.Exists(path))
            {
                File.Move(path, aside);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves <paramref name="fresh"/>, made in <c>update\</c>, in as <paramref name="version"/>'s folder, setting aside the
    /// one there; false, moving nothing, when that one is in use.
    /// </summary>
    public bool Place(string fresh, ReleaseVersion version)
    {
        var target = VersionOf(version);
        if (Directory.Exists(target) && !SetAside(target))
        {
            return false;
        }

        Directory.Move(fresh, target);
        return true;
    }

    public ReleaseVersion? AtTop() => Directory.EnumerateDirectories(top)
        .Select(path => ReleaseVersion.Parse(Path.GetFileName(path)))
        .FirstOrDefault(version => version is not null
            && Same(Path.Combine(top, "thermi.exe"), Path.Combine(VersionOf(version), "launchers", "thermi.exe")));

    private static bool Same(string one, string other) =>
        File.Exists(one) && File.Exists(other) && File.ReadAllBytes(one).AsSpan().SequenceEqual(File.ReadAllBytes(other));

    /// <summary>
    /// Puts <paramref name="version"/>'s launchers at the top, should they be missing or another's: the one there is set
    /// aside, which Windows allows while a process runs from it, and the new one copied beside and moved in. True when any changed.
    /// </summary>
    public bool PlaceLaunchers(ReleaseVersion version)
    {
        var changed = false;
        foreach (var launcher in Directory.EnumerateFiles(Path.Combine(VersionOf(version), "launchers"), "*.exe"))
        {
            var placed = Path.Combine(top, Path.GetFileName(launcher));
            if (Same(placed, launcher))
            {
                continue;
            }

            var next = placed + ".new";
            File.Copy(launcher, next, overwrite: true);
            if (File.Exists(placed) && !SetAside(placed))
            {
                throw new IOException($"The launcher {placed} could not be set aside.");
            }

            File.Move(next, placed);
            changed = true;
        }

        return changed;
    }

    private void EmptyTrash()
    {
        if (!Directory.Exists(Trash))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(Trash))
        {
            Delete(entry);
        }
    }

    /// <summary>Deletes a file or a folder; one a process still runs from, or an antivirus holds, stays for a later try.</summary>
    public static void Delete(string entry)
    {
        try
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }
}
