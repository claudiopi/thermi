using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Windows.Tests;

/// <summary>
/// A settings file of the test's own under the test output, its stores and settings, and a fake clock to step them by;
/// both test projects build it, and the Windows tests open a window on it (<c>SettingsWindows.cs</c>).
/// </summary>
internal sealed class SettingsFolder : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "settings-files", Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _stores = [];

    public SettingsFolder()
    {
        Directory.CreateDirectory(_folder);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 22, 15, 30, 45, TimeSpan.Zero));

    public string File => Path.Combine(_folder, "config.json");

    public string MutexName => ConfigStore.MutexNameOf(File);

    /// <summary>An environment that gives a thermi process a profile under this folder, never the user's settings or logs.</summary>
    public IReadOnlyDictionary<string, string> Profile => new Dictionary<string, string>
    {
        ["APPDATA"] = Path.Combine(_folder, "roaming"),
        ["LOCALAPPDATA"] = Path.Combine(_folder, "local"),
    };

    /// <summary>What the settings and their watchers logged.</summary>
    public ListLogger Log { get; } = new();

    public IEnumerable<string> Files => Directory.EnumerateFiles(_folder).Select(Path.GetFileName).OfType<string>();

    public static string Sized(double size) => ThermiConfigJson.Write(OverlayEdits.SetSize(new ThermiConfig(), 0, size).Config);

    public ConfigStore Store()
    {
        var store = new ConfigStore(File, Clock, installed: () => true);
        _stores.Add(store);
        return store;
    }

    /// <summary>Settings on the fake clock, or on <paramref name="time"/> when given, which steps with it.</summary>
    public Settings Settings(TimeProvider? time = null) => new(Store(), time ?? Clock, Log);

    public async Task<Settings> LoadedAsync(TimeProvider? time = null)
    {
        var settings = Settings(time);
        await Settled(settings.SyncAsync);
        return settings;
    }

    /// <summary>The settings of <paramref name="config"/>, written to the file and read back.</summary>
    public Task<Settings> LoadedAsync(ThermiConfig config)
    {
        Write(ThermiConfigJson.Write(config));
        return LoadedAsync();
    }

    public void Write(string text) => System.IO.File.WriteAllText(File, text);

    /// <summary>The file as written so far, or null while a write moves it into place.</summary>
    public string? Written()
    {
        try
        {
            return Text();
        }
        catch (IOException)
        {
            return null;
        }
    }

    public string Text() => System.IO.File.ReadAllText(File);

    public ThermiConfig Read() => ThermiConfigJson.Read(Text()).Config;

    /// <summary>Starts the work, and steps the clock a poll at a time, past any retry and the mutex's timeout, until it ends.</summary>
    public Task<T> Settled<T>(Func<Task<T>> start)
    {
        var task = start();
        RunOut(task);
        return task;
    }

    public Task Settled(Func<Task> start)
    {
        var task = start();
        RunOut(task);
        return task;
    }

    /// <summary>
    /// Steps the clock a poll at a time, at most 100 times, until <paramref name="task"/> has ended: for work this thread
    /// drives alone, which the caller then awaits.
    /// </summary>
    public void RunOut(Task task)
    {
        for (var step = 0; !task.IsCompleted && step < 100; step++)
        {
            Clock.Advance(ConfigStore.Poll);
        }
    }

    /// <summary>
    /// Steps the clock, with a moment of real time between steps for work that waits on another thread, while
    /// <paramref name="task"/> runs; the caller then awaits it. The steps alone outrun such work, as a sync that waits its
    /// turn behind the throttle's, and would leave it unfinished while the test reads what it was to write. No step is
    /// taken while a writer of this process holds the file's mutex: one the PC held back mid-write would have the clock
    /// run another writer out of its wait for it. A <see cref="MutexHolder"/>, which stands for another process, is
    /// waited out as the clock moves.
    /// </summary>
    public async Task StepWhileAsync(Task task)
    {
        using var probe = new Mutex(initiallyOwned: false, MutexName);
        var start = TimeProvider.System.GetTimestamp();
        for (var step = 0; !task.IsCompleted && step < 2000 && TimeProvider.System.GetElapsedTime(start) < Patience;)
        {
            if (Free(probe) || MutexHolder.Holds(MutexName))
            {
                Clock.Advance(TimeSpan.FromMilliseconds(100));
                step++;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System);
        }
    }

    // Taken and given back at once on this thread, as a mutex is the thread's that takes it.
    private static bool Free(Mutex probe)
    {
        try
        {
            if (!probe.WaitOne(0))
            {
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // Ours now, as it would be the next writer's.
        }

        probe.ReleaseMutex();
        return true;
    }

    /// <summary>Steps the clock a poll at a time for <paramref name="span"/>.</summary>
    public void Pass(TimeSpan span)
    {
        for (var passed = TimeSpan.Zero; passed < span; passed += ConfigStore.Poll)
        {
            Clock.Advance(ConfigStore.Poll);
        }
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        Directory.Delete(_folder, recursive: true);
    }
}

/// <summary>
/// Another writer holding the settings file's mutex, on a thread of its own as another process would: a mutex is the
/// thread's that took it, and the test's own thread would take it again. One that dies leaves it abandoned.
/// </summary>
internal sealed class MutexHolder : IDisposable
{
    // The names a holder holds now, each with how many hold it.
    private static readonly ConcurrentDictionary<string, int> Held = new();

    private readonly ManualResetEventSlim _taken = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;
    private readonly string? _name;
    private int _released;
    private volatile bool _crash;

    private MutexHolder(string name, bool dies)
    {
        _thread = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, name);
            mutex.WaitOne();
            _taken.Set();
            if (!dies)
            {
                _release.Wait();
                if (!_crash)
                {
                    mutex.ReleaseMutex();
                }
            }
        });
        _thread.Start();
        _taken.Wait();
        if (dies)
        {
            _thread.Join();
            return;
        }

        _name = name;
        Held.AddOrUpdate(name, 1, (_, holders) => holders + 1);
    }

    /// <summary>Whether a holder holds the mutex of <paramref name="name"/> now.</summary>
    public static bool Holds(string name) => Held.TryGetValue(name, out var holders) && holders > 0;

    public static MutexHolder Holding(string name) => new(name, dies: false);

    public static void DiesHolding(string name)
    {
        using var _ = new MutexHolder(name, dies: true);
    }

    /// <summary>Ends the holder without releasing the mutex, as a process that crashes: its handle closes with it.</summary>
    public void Crash()
    {
        _crash = true;
        Release();
    }

    public void Release()
    {
        _release.Set();
        _thread.Join();
        if (_name is not null && Interlocked.Exchange(ref _released, 1) == 0)
        {
            Held.AddOrUpdate(_name, 0, (_, holders) => holders - 1);
        }
    }

    public void Dispose()
    {
        Release();
        _taken.Dispose();
        _release.Dispose();
    }
}
