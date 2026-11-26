using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.App.SettingsFile;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Translations;
using Thermi.Windows.Commands;
using Thermi.Windows.Install;
using Thermi.Windows.Logging;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui.Web;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;

namespace Thermi.Windows.Ui;

/// <summary>
/// What the window reaches outside its process, which tests replace: its single-instance mutex, the settings file, the
/// executable the overlay starts from, the log, its debug events with <c>--trace</c>, when it was asked for, which is when
/// its process started, the sensors, read from the pipe as the overlay reads them, the start-up entry that "Start with
/// Windows" switches, the overlay's "hotkey status" and its hotkeys' rest, the elevated mode and the monitors.
/// </summary>
internal sealed record WindowEnvironment(
    string Mutex,
    string SettingsPath,
    string ExePath,
    ILogger Log,
    DateTimeOffset Requested,
    ISensorFeed Sensors,
    StartWithWindows Startup)
{
    /// <summary>Where the window keeps WebView2's profile and its size and place: beside the log.</summary>
    public string DataFolder { get; init; } = ThermiLog.Folder;

    /// <summary>Where WebView2's runtime is looked for: null for the one installed, wherever Windows has it.</summary>
    public string? BrowserFolder { get; init; }

    /// <summary>Asks the overlay which hotkeys it could not register, null when none answers.</summary>
    public Func<long?> HotkeyStatus { get; init; } = OverlayHost.AskHotkeyStatus;

    /// <summary>Asks the overlay to let its hotkeys rest, or take them again.</summary>
    public Func<bool, bool> RestHotkeys { get; init; } = HotkeyRegistry.AskToRest;

    /// <summary>The keys AltGr types a character with on the keyboard layout now, which no Ctrl+Alt hotkey may take.</summary>
    public Func<IReadOnlySet<int>> AltGrKeys { get; init; } = HotkeyRegistry.AltGrKeys;

    /// <summary>The elevated mode's verb, behind its UAC prompt: its exit code, or null when the prompt was refused.</summary>
    public Func<string, Task<int?>> Elevate { get; init; } = Elevated.AskAsync;

    /// <summary>The monitors, their taskbars and notification areas, as Windows has them now.</summary>
    public Func<IReadOnlyList<Screen>> Screens { get; init; } = Desktop.Screens;
    public static WindowEnvironment Of(ILoggerFactory logs, TimeProvider time)
    {
        using var process = Process.GetCurrentProcess();
        var exe = ThermiPaths.Launcher;
        return new(
            HostEnvironment.WindowMutexName,
            ConfigStore.DefaultPath,
            exe,
            logs.CreateLogger("Thermi.Ui"),
            new DateTimeOffset(process.StartTime),
            new SystemFeed(
                new SensorClient(time, logs.CreateLogger<SensorClient>(), new SensorPipes(time, logs.CreateLogger<SensorPipes>(), starts: false)), time),
            StartWithWindows.Of(exe));
    }
}

/// <summary>
/// What the window opens with: the overlay asked for, counted from 1 or 0 for none, the settings, read once already, its
/// texts, what it reaches, the clock, and the fan finder's load, which tests replace so they never load the CPU.
/// </summary>
internal sealed record WindowStart(int Overlay, Settings Settings, Texts Texts, WindowEnvironment Environment, TimeProvider Time)
{
    public Action<CancellationToken> Load
    {
        get => field ?? StartLoad;
        init;
    }

    /// <summary>True with <c>--pseudo</c>, whose texts stay whatever language the settings choose.</summary>
    public bool Pseudo { get; init; }

    /// <summary>The item of <see cref="Overlay"/> the window opens on, as a double-click on a value asks, or null for the overlay.</summary>
    public string? Item { get; init; }

    private void StartLoad(CancellationToken stop) => CpuLoad.Start(Environment.ExePath, (int)Core.Sensors.FanFinder.MaxRun.TotalSeconds, stop);
}

/// <summary>
/// <c>thermi settings</c>: the window, its page in WebView2, on an STA thread of its own, since the overlay's entry point
/// is async. Only one is open: it holds its mutex while it is, and a second request brings it to the front instead, then
/// exits. The window never loads WPF.
/// </summary>
internal static class WindowProcess
{
    // A window that has just taken the mutex needs a moment before its hidden window can be found.
    private static readonly TimeSpan FindFor = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(int overlay, WindowEnvironment environment, bool pseudo, TimeProvider time, string? item = null)
    {
        if (await ShowAsync(overlay, item, environment, pseudo, time))
        {
            return 0;
        }

        for (var start = time.GetTimestamp(); time.GetElapsedTime(start) < FindFor; await Task.Delay(ConfigStore.Poll, time))
        {
            if (SettingsBeacon.AskToRaise(overlay, item))
            {
                return 0;
            }
        }

        return 1;
    }

    // The settings are read before the window opens, for its language and its overlays, and every write that waits goes
    // out once it has closed. The sensors are read only while it is open. What fails on the window's thread is thrown
    // again here, so the process's entry logs it.
    private static async Task<bool> ShowAsync(int overlay, string? item, WindowEnvironment environment, bool pseudo, TimeProvider time)
    {
        using var store = new ConfigStore(environment.SettingsPath, time, ThermiPaths.RunsInstalled);
        await using var settings = new Settings(store, time, environment.Log);
        await settings.SyncAsync();
        using var watcher = settings.Watch(environment.Log);
        using var stop = new CancellationTokenSource();
        var reading = Task.CompletedTask;
        var texts = pseudo ? Texts.Pseudo() : Texts.For(settings.Config.Language, CultureInfo.CurrentUICulture);
        var opened = false;
        ExceptionDispatchInfo? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                opened = Open(
                    new WindowStart(overlay, settings, texts, environment, time) { Pseudo = pseudo, Item = item },
                    () => reading = Task.Run(() => environment.Sensors.RunAsync(stop.Token), stop.Token));
            }
#pragma warning disable CA1031 // Not handled: thrown again on the caller's thread, which a thread's own failure would never reach.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        await stop.CancelAsync();
        await reading.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        failed?.Throw();
        return opened;
    }

    /// <summary>Takes the window's mutex, a crashed window's included, or gives null while an open window holds it.</summary>
    internal static Mutex? Hold(string name)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            if (mutex.WaitOne(0))
            {
                return mutex;
            }
        }
        catch (AbandonedMutexException)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    // The mutex is let go on the thread that took it, as a mutex must be, once the window has closed.
    private static bool Open(WindowStart start, Action read)
    {
        using var mutex = Hold(start.Environment.Mutex);
        if (mutex is null)
        {
            return false;
        }

        try
        {
            read();
            WindowSession.Run(start);
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        return true;
    }
}
