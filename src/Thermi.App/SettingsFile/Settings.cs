using Microsoft.Extensions.Logging;
using Thermi.Core;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.App.Logging;

namespace Thermi.App.SettingsFile;

/// <summary>
/// The overlay's settings as the user's file holds them. Every read and write goes through one throttle, off the
/// caller's thread once it has to wait, so the overlay never waits on the file. While the file cannot be used, the last
/// good settings stay on screen and the file is never written: it holds the user's edits. A drag of any overlay made
/// meanwhile, or one that could not be written yet, stays on screen and is written once the file takes it; a click is never written.
/// </summary>
internal sealed class Settings : IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a write that keeps failing, as on a read-only file, waits between tries after the first few.</summary>
    public static readonly TimeSpan Backoff = TimeSpan.FromSeconds(1);

    /// <summary>How often a file that cannot be used is read again: a lock let go without a write raises no change of the file.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(1);

    private readonly ConfigStore _store;
    private readonly ILogger _log;
    private readonly Throttle<bool> _syncs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly PendingWrites _pending = new();
    private readonly ITimer _later;
    private State? _state;

    // What the file held at the last read or write that found it good: what is shown is always this, then the drags and
    // edits that wait, so a read that finds the file broken never applies them twice.
    private ThermiConfig _read = new();
    private int _failures;

    // Asked to put the last good settings back over a broken file, until a write has.
    private bool _restoring;

    // Whether a drag or an edit changed what is shown since Changed was last raised.
    private bool _unannounced;
    private volatile bool _closing;

    public Settings(ConfigStore store, TimeProvider time, ILogger log)
    {
        _store = store;
        _log = log;
        _syncs = new Throttle<bool>(Interval, ignored => _ = SyncGuardedAsync(), time);
        _later = time.CreateTimer(_ => Again(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised on any thread once the settings or their problem changed, the first read included; a read that finds what
    /// the overlay last wrote or read, or the file broken in the same way again, raises nothing.
    /// </summary>
    public event EventHandler? Changed;

    public string FilePath => _store.FilePath;

    /// <summary>False until the file was first read, so nothing shows the defaults while it may hold something else.</summary>
    public bool Loaded => _state is not null;

    public ThermiConfig Config => _state?.Config ?? new ThermiConfig();

    /// <summary>Why the file cannot be used, or null when it can.</summary>
    public ConfigProblem? Problem => _state?.Problem;

    /// <summary>The file changed, or may have: it is read again through the throttle.</summary>
    public void FileChanged() => _syncs.Post(true);

    /// <summary>
    /// Reads the file again whenever it changes, until the watcher is disposed. Without access to its folder the overlay
    /// still runs, with the defaults, and the config problem says why.
    /// </summary>
    public FileSystemWatcher? Watch(ILogger log) => SettingsWatcher.Watch(FilePath, FileChanged, log);

    /// <summary>
    /// A press on an overlay, found by its place in the settings, let go at <paramref name="released"/>: a drag past
    /// <paramref name="distance"/> moves it off its snap at once, and is written through the throttle. A click, or any
    /// press on a locked overlay, changes and writes nothing. Returns the settings with that drop, or null when there was
    /// none.
    /// </summary>
    public ThermiConfig? Release(Press press, Position released, DragDistance distance, int overlay = 0)
    {
        ThermiConfig after;
        lock (_lock)
        {
            if (OverlayEdits.Release(Config, overlay, press, released, distance) is not { Refused: null } dropped
                || dropped.Config.Overlays[overlay].Items is not [var first, ..])
            {
                return null;
            }

            after = dropped.Config;
            _pending.Drop(first.Id, after.Overlays[overlay].Position!);
            _state = new State(_pending.Over(_read), Problem);
            _unannounced = true;
        }

        _syncs.Post(true);
        return after;
    }

    /// <summary>
    /// A change the overlay makes itself, as the next-style hotkey does: shown at once, and written through the throttle
    /// as a change of whatever the file holds then, so it never undoes another process's.
    /// </summary>
    public void Edit(Func<ThermiConfig, ThermiConfig> edit)
    {
        lock (_lock)
        {
            _pending.Add(edit);
            _state = new State(_pending.Over(_read), Problem);
            _unannounced = true;
        }

        _syncs.Post(true);
    }

    /// <summary>
    /// Keeps the file as it is beside it, under a dated name, and writes the last settings read well in this process, or
    /// the defaults if there were none, through the throttle; the drags and edits that wait are written over them after.
    /// A file that reads well before the restore lands, fixed by hand or unlocked, is left as it is and shown instead.
    /// </summary>
    public void Restore()
    {
        lock (_lock)
        {
            _restoring = true;
        }

        _syncs.Post(true);
    }

    /// <summary>
    /// Restores, or writes the drags and edits that wait, which re-reads the file under its mutex, or else reads the file.
    /// </summary>
    public async Task SyncAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        TimeSpan? again = null;
        try
        {
            var waiting = Waiting();
            again = Restoring() is { } good ? await RestoreAsync(good).ConfigureAwait(false)
                : waiting.Any ? await SaveAsync(waiting).ConfigureAwait(false)
                : await LoadAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (again == TimeSpan.Zero)
        {
            Again();
        }
        else if (again is { } later)
        {
            Retry(later);
        }
    }

    /// <summary>What still waits goes out, and the work under way ends, before the store can be let go.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _closing = true;
            _later.Dispose();
        }

        _syncs.Dispose();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task SyncGuardedAsync()
    {
        try
        {
            await SyncAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The throttle has no caller to throw to: what escapes a sync is logged, and the next change tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.SettingsFailed(ex);
        }
    }

    private ThermiConfig? Restoring()
    {
        lock (_lock)
        {
            return _restoring ? _read : null;
        }
    }

    private PendingWrites.Waiting Waiting()
    {
        lock (_lock)
        {
            return _pending.Now();
        }
    }

    // Under the lock quitting takes, so a write that fails while it begins never sets the timer it let go.
    private void Retry(TimeSpan later)
    {
        lock (_lock)
        {
            if (!_closing)
            {
                _later.Change(later, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Again()
    {
        if (!_closing)
        {
            _syncs.Post(true);
        }
    }

    // A file that cannot be used is read again a while later, so it recovers on its own once fixed or unlocked.
    private async Task<TimeSpan?> LoadAsync()
    {
        var read = await _store.ReadAsync().ConfigureAwait(false);
        if (read.Problem is not null)
        {
            Show(read.Problem);
            return Recheck;
        }

        if (read.Outside || !Loaded || Problem is not null)
        {
            Show(null, read.Config);
        }

        return null;
    }

    // How long until the write is tried again, or null when it is done with.
    private async Task<TimeSpan?> SaveAsync(PendingWrites.Waiting waiting)
    {
        var write = await _store.WriteAsync(waiting.Over).ConfigureAwait(false);
        switch (write.Outcome)
        {
            case WriteOutcome.Written:
                _failures = 0;
                Show(null, write.Config, waiting);
                return null;
            case WriteOutcome.Broken:
                Show(write.Problem);
                return Recheck;
            case WriteOutcome.Retry when write.Problem is not null:
                Show(write.Problem);
                return Failed(write.Error);
            case WriteOutcome.Retry:
            case WriteOutcome.Good:
            default:
                return Failed(write.Error);
        }
    }

    // Once written, or once the file reads well again and is left as it is, what waits is written over it at once.
    private async Task<TimeSpan?> RestoreAsync(ThermiConfig good)
    {
        var write = await _store.RestoreAsync(good).ConfigureAwait(false);
        if (write.Outcome is not (WriteOutcome.Written or WriteOutcome.Good))
        {
            return Failed(write.Error);
        }

        lock (_lock)
        {
            _restoring = false;
        }

        _failures = 0;
        Show(null, write.Config);
        return Waiting().Any ? TimeSpan.Zero : null;
    }

    // A write the OS refuses, as on a read-only file or a folder without access, may never land: the first failure is
    // logged, and after a few quick tries it waits a second between them. A busy mutex, or a lock an ordinary write meets,
    // waits on its own.
    private TimeSpan Failed(Exception? error)
    {
        const int QuickTries = 3;
        if (error is null)
        {
            return TimeSpan.Zero;
        }

        if (_failures++ == 0)
        {
            _log.SettingsFailed(error);
        }

        return _failures > QuickTries ? Backoff : TimeSpan.Zero;
    }

    // A drag or an edit still waiting to be written stays on screen over what the file held when it was last good. What
    // was just written is forgotten in the same step, so a drag or an edit meanwhile never shows the settings without it.
    private void Show(ConfigProblem? problem, ThermiConfig? read = null, PendingWrites.Waiting? written = null)
    {
        bool changed;
        lock (_lock)
        {
            if (written is not null)
            {
                _pending.Written(written);
            }

            changed = read is not null || _unannounced || !Loaded || Problem != problem;
            _unannounced = false;
            _read = read ?? _read;
            _state = new State(_pending.Over(_read), problem);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record State(ThermiConfig Config, ConfigProblem? Problem);
}
