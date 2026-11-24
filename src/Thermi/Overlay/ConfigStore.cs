using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Thermi.Core.Settings;
using Thermi.Logging;

namespace Thermi.Overlay;

/// <summary>What a read found: the settings, why the file cannot be used, and whether it holds other than what this store last wrote or read.</summary>
internal sealed record ConfigRead(ThermiConfig Config, ConfigProblem? Problem, bool Outside);

internal enum WriteOutcome
{
    Written,

    /// <summary>The file is broken: nothing was written over it.</summary>
    Broken,

    /// <summary>The mutex was not free in time, the file stayed locked, or writing or moving into place failed: try again.</summary>
    Retry,

    /// <summary>A restore found the file reading well, as once fixed by hand or no longer locked: nothing was written over it.</summary>
    Good,
}

/// <summary>What a write did: what it wrote, or why it wrote nothing, with what Windows said when it refused the write itself.</summary>
internal sealed record ConfigWrite(WriteOutcome Outcome, ThermiConfig? Config = null, ConfigProblem? Problem = null, Exception? Error = null);

/// <summary>
/// The settings file, shared by every process of thermi. A writer holds the file's named mutex from its re-read to its
/// move into place and applies its own change to what it found there, so no write loses another's; the work under the
/// mutex is synchronous, so the thread that takes it releases it. Readers share read, write and delete, so a read never
/// blocks a move into place. A file found locked, empty, cut short or missing, as while an editor saves, is tried again
/// for a while before it counts. The waits are on the injected clock.
/// </summary>
internal sealed class ConfigStore : IDisposable
{
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    public static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(2);

    private const string Temporary = ".tmp";

    private static readonly ConfigWrite Again = new(WriteOutcome.Retry);

    private readonly TimeProvider _time;
    private readonly Mutex _mutex;

    // The text this store last wrote or read, so its own write is no outside change and the same text coming back after
    // another is one.
    private volatile string? _known;

    public ConfigStore(string path, TimeProvider time)
    {
        FilePath = Path.GetFullPath(path);
        _time = time;
        _mutex = new Mutex(initiallyOwned: false, MutexNameOf(FilePath));
    }

    private enum Found
    {
        Whole,
        Unfinished,
        Missing,
        Locked,
    }

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "thermi", "config.json");

    public string FilePath { get; }

    /// <summary>The saved styles' folder, <c>styles</c> beside the settings file, which the next-style hotkey and the window both read.</summary>
    public static string StylesFolder(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "styles");

    private string Folder => Path.GetDirectoryName(FilePath)!;

    /// <summary>Named after the file, so a test on a file of its own never waits on the user's.</summary>
    internal static string MutexNameOf(string path) =>
        @"Local\thermi-settings-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..16];

    /// <summary>How a reader opens the file: sharing everything, so a writer's move into place never fails on it.</summary>
    internal static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// The settings in the file, or the defaults and why it cannot be used. A file still missing once the retry is over
    /// is created with the defaults, so there is something to edit.
    /// </summary>
    public async Task<ConfigRead> ReadAsync()
    {
        var found = await SettledAsync().ConfigureAwait(false);
        if (found.Kind == Found.Missing && (await WriteAsync(Create, settled: true).ConfigureAwait(false)).Config is { } created)
        {
            return new ConfigRead(created, null, Outside: false);
        }

        return ReadOf(found.Kind == Found.Missing ? Peek() : found);
    }

    /// <summary>The settings in the file as <see cref="ReadAsync"/> gives them, or the defaults when it is missing, which is left missing.</summary>
    public async Task<ConfigRead> ReadOnlyAsync()
    {
        var found = await SettledAsync().ConfigureAwait(false);
        return found.Kind == Found.Missing ? new ConfigRead(new ThermiConfig(), null, Outside: true) : ReadOf(found);
    }

    /// <summary>Applies <paramref name="change"/> to the settings the file holds now and writes the result, never over a broken file.</summary>
    public Task<ConfigWrite> WriteAsync(Func<ThermiConfig, ThermiConfig> change) => WriteAsync(found => Changed(found, change), settled: false);

    /// <summary>
    /// Keeps the file as it is beside it, under a dated name, and writes <paramref name="good"/> in its place; a file that
    /// reads well by then is left as it is, since whoever fixed or unlocked it meant what it holds.
    /// </summary>
    public Task<ConfigWrite> RestoreAsync(ThermiConfig good) => WriteAsync(found => Restored(found, good), settled: false);

    public void Dispose() => _mutex.Dispose();

    // Without access to its folder the overlay still runs, with the defaults, and the config problem says why. On a first
    // run the folder is made here, since the file is made only once a read has found it missing.
    internal static FileSystemWatcher? Watch(string file, Action changed, ILogger log)
    {
        var watcher = new FileSystemWatcher();
        try
        {
            watcher.Path = Directory.CreateDirectory(Path.GetDirectoryName(file)!).FullName;
            watcher.Filter = Path.GetFileName(file);

            // Editors save in different ways: in place, or by writing another file and renaming it over this one.
            watcher.Changed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            watcher.Dispose();
            log.WatchFailed(ex);
            return null;
        }
    }

    private static Plan Create(Look found) => new(found.Kind == Found.Missing ? new ThermiConfig() : null);

    private static Plan Changed(Look found, Func<ThermiConfig, ThermiConfig> change)
    {
        if (found.Kind == Found.Locked)
        {
            return new Plan(null, Again);
        }

        var (config, problem) = ThermiConfigJson.Read(found.Text);
        return problem is null ? new Plan(change(config)) : new Plan(null, new ConfigWrite(WriteOutcome.Broken, Problem: problem));
    }

    // A restore is asked for over a file that cannot be used, so a lock that outlasts an editor's save is why it does not
    // land, and it is said as a write Windows refused.
    private Plan Restored(Look found, ThermiConfig good)
    {
        if (found.Kind == Found.Locked)
        {
            return new Plan(null, new ConfigWrite(WriteOutcome.Retry, Error: found.Lock));
        }

        if (found.Kind == Found.Missing)
        {
            return new Plan(good);
        }

        return found.Kind == Found.Whole && Parsed(found.Text) is { Problem: null } read
            ? new Plan(null, new ConfigWrite(WriteOutcome.Good, read.Config))
            : new Plan(good, Keep: true);
    }

    private async Task<Look> SettledAsync()
    {
        var started = _time.GetTimestamp();
        var found = Peek();
        while (found.Kind != Found.Whole && _time.GetElapsedTime(started) < Settle)
        {
            await Task.Delay(Poll, _time).ConfigureAwait(false);
            found = Peek();
        }

        return found;
    }

    // Waiting for the mutex gives up after its timeout; a file found unsettled under it is let go and waited out first.
    private async Task<ConfigWrite> WriteAsync(Func<Look, Plan> plan, bool settled)
    {
        var started = _time.GetTimestamp();
        long? unsettled = null;
        while (true)
        {
            var (done, busy) = TryWrite(plan, settled || (unsettled is { } since && _time.GetElapsedTime(since) >= Settle));
            if (done is not null)
            {
                return done;
            }

            if (!busy)
            {
                unsettled ??= _time.GetTimestamp();
            }
            else if (_time.GetElapsedTime(started) >= MutexTimeout)
            {
                return Again;
            }

            await Task.Delay(Poll, _time).ConfigureAwait(false);
        }
    }

    // Null and busy when the mutex is taken, null and not busy when the file is unsettled and not yet waited out.
    private (ConfigWrite? Done, bool Busy) TryWrite(Func<Look, Plan> plan, bool settled)
    {
        if (!Acquire())
        {
            return (null, true);
        }

        try
        {
            DeleteTemporaries();
            var found = Peek();
            if (found.Kind != Found.Whole && !settled)
            {
                return (null, false);
            }

            var decided = plan(found);
            return (decided.Next is { } next ? Commit(next, decided.Keep) : decided.Instead ?? Again, false);
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    // A writer that died holding the mutex leaves it abandoned, and the one that finds it owns it.
    private bool Acquire()
    {
        try
        {
            return _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    // A write that fails takes its copy of the file back, so a restore retried once a second never piles up copies.
    private ConfigWrite Commit(ThermiConfig next, bool keep)
    {
        var text = ThermiConfigJson.Write(next);
        var temporary = $"{FilePath}.{Guid.NewGuid():N}{Temporary}";
        string? kept = null;
        try
        {
            Directory.CreateDirectory(Folder);
            if (keep)
            {
                kept = CopyAside();
            }

            File.WriteAllText(temporary, text);
            Replace(temporary);
            _known = text;
            return new ConfigWrite(WriteOutcome.Written, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Delete(temporary);
            if (kept is not null)
            {
                Delete(kept);
            }

            return new ConfigWrite(WriteOutcome.Retry, Error: ex);
        }
    }

    // The copy is thermi's own, so a read-only file's is made writable, or a failed write could not take it back.
    private string CopyAside()
    {
        var kept = KeptName();
        File.Copy(FilePath, kept);
        File.SetAttributes(kept, File.GetAttributes(kept) & ~FileAttributes.ReadOnly);
        return kept;
    }

    // A move fails over a file a reader holds open, even one shared for delete; a replace does not.
    private void Replace(string temporary)
    {
        if (File.Exists(FilePath))
        {
            File.Replace(temporary, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporary, FilePath, overwrite: true);
        }
    }

    // Under the mutex no live writer is midway through a write, so a writer's file left beside this one is a dead writer's.
    private void DeleteTemporaries()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder, $"{Path.GetFileName(FilePath)}.*{Temporary}").Where(file => file.EndsWith(Temporary, StringComparison.OrdinalIgnoreCase)))
            {
                Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has nothing to clean that the write could reach either.
        }
    }

    private static void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next writer.
        }
    }

    private string KeptName()
    {
        var stem = Path.Combine(Folder, $"{Path.GetFileNameWithoutExtension(FilePath)}.broken-{_time.GetLocalNow().ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}");
        var extension = Path.GetExtension(FilePath);
        var name = stem + extension;
        var repeat = 1;
        while (File.Exists(name))
        {
            repeat++;
            name = string.Create(CultureInfo.InvariantCulture, $"{stem}-{repeat}{extension}");
        }

        return name;
    }

    private Look Peek()
    {
        try
        {
            using var stream = OpenShared(FilePath);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return new Look(ThermiConfigJson.IsUnfinished(text) ? Found.Unfinished : Found.Whole, text);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Look(Found.Missing, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Look(Found.Locked, ex.Message, ex);
        }
    }

    private ConfigRead ReadOf(Look found) => found.Kind switch
    {
        Found.Locked => new ConfigRead(new ThermiConfig(), ConfigProblem.Locked(found.Text), Outside: true),
        Found.Missing => new ConfigRead(new ThermiConfig(), ConfigProblem.Missing, Outside: true),
        Found.Whole or Found.Unfinished => Parsed(found.Text),
        _ => throw new ArgumentOutOfRangeException(nameof(found), found.Kind, "Unknown kind of read."),
    };

    private ConfigRead Parsed(string text)
    {
        var (config, problem) = ThermiConfigJson.Read(text);
        var outside = text != _known;
        _known = text;
        return new ConfigRead(config, problem, outside);
    }

    /// <summary>What a look at the file found, with what Windows said when it was locked.</summary>
    private readonly record struct Look(Found Kind, string Text, Exception? Lock = null);

    /// <summary>What a write puts in place, keeping the file it found beside it, or what it gives instead of writing.</summary>
    private sealed record Plan(ThermiConfig? Next, ConfigWrite? Instead = null, bool Keep = false);
}
