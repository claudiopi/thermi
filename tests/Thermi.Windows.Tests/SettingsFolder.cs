using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Ui.Web;

namespace Thermi.Windows.Tests;

/// <summary>A settings file of the test's own under the test output, its stores and settings, and a fake clock to step them by.</summary>
internal sealed class SettingsFolder : IDisposable
{
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
        var store = new ConfigStore(File, Clock);
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

    /// <summary>
    /// What a window on this file reaches, with <paramref name="sensors"/> for the pipe, a start-up entry of the tests' own,
    /// no monitor and no overlay to answer for its hotkeys.
    /// </summary>
    public WindowEnvironment Window(ISensorFeed sensors)
    {
        var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi.exe");
        return new("thermi-tests", File, "thermi.exe", Log, Clock.GetUtcNow(), sensors, startup) { Screens = () => [], HotkeyStatus = () => null };
    }

    /// <summary>
    /// A window's bridge on <paramref name="settings"/>, opened on <paramref name="overlay"/>, its editor and its turns on
    /// <paramref name="turn"/>, on this file's clock.
    /// </summary>
    public WindowBridge Bridge(Settings settings, WindowEnvironment window, ConcurrentQueue<Action> turn, int overlay = 0, ChartHistory? chart = null)
    {
        var start = new WindowStart(overlay, settings, Texts.English, window, Clock);
        return new(start, new WindowEditor(settings, turn.Enqueue, Log, Clock), chart ?? new(Clock), turn.Enqueue);
    }

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

    /// <summary>Steps the clock a poll at a time until <paramref name="task"/> has ended.</summary>
    public void RunOut(Task task)
    {
        for (var step = 0; !task.IsCompleted && step < 100; step++)
        {
            Clock.Advance(ConfigStore.Poll);
        }
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
    private readonly ManualResetEventSlim _taken = new();
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;
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
        }
    }

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
    }

    public void Dispose()
    {
        Release();
        _taken.Dispose();
        _release.Dispose();
    }
}
