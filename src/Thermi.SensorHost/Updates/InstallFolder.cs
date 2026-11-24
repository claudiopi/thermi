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

    /// <summary>Notes that another program held the pipe's name as this service started; never throws.</summary>
    public void MarkPipeHeld()
    {
        try
        {
            File.WriteAllText(PipeHeldPath, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Outside an install folder only administrators change, or with update\ gone: no switch waits on it.
        }
    }

    public bool PipeHeld() => File.Exists(PipeHeldPath);

    public void ForgetPipeHeld() => File.Delete(PipeHeldPath);

    public UpdateState Read() => UpdateState.Read(File.Exists(StatePath) ? File.ReadAllText(StatePath) : null);

    /// <summary>Writes the state whole or not at all: to a file of its own, flushed to the disk, then moved over the old one.</summary>
    public void Write(UpdateState state)
    {
        var next = StatePath + ".new";
        using (var file = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(file))
        {
            writer.Write(state.Write());
            writer.Flush();
            file.Flush(flushToDisk: true);
        }

        File.Move(next, StatePath, overwrite: true);
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
    /// Puts <paramref name="version"/>'s launchers at the top, should they be missing or another's: the one there is set
    /// aside, which Windows allows while a process runs from it, and the new one copied beside and moved in. True when any changed.
    /// </summary>
    public bool PlaceLaunchers(ReleaseVersion version)
    {
        var changed = false;
        foreach (var launcher in Directory.EnumerateFiles(Path.Combine(VersionOf(version), "launchers"), "*.exe"))
        {
            var placed = Path.Combine(top, Path.GetFileName(launcher));
            if (File.Exists(placed) && File.ReadAllBytes(placed).AsSpan().SequenceEqual(File.ReadAllBytes(launcher)))
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
                // A launcher a process still runs from, or a file an antivirus holds: the next prune tries again.
            }
        }
    }
}
