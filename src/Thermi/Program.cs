using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Updates;
using Thermi.Install;
using Thermi.Logging;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Updates;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.System.Console;

// Windows 10 1607 and later, which every Win32 call thermi makes needs (Thermi.csproj says why it is set here).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0.14393")]

namespace Thermi;

internal static class Program
{
    /// <summary>The overlay's single-instance mutex: a second thermi opens the window instead.</summary>
    internal const string OverlayMutex = @"Local\thermi-overlay";

    // Arguments that make no sense say why on stderr and exit with 2, before anything else runs.
    private static async Task<int> Main(string[] args)
    {
        var time = TimeProvider.System;
        var request = ThermiCommands.Parse(args, out var errors);
        if (request is null)
        {
            CommandLineOutput.Borrow(STD_HANDLE.STD_ERROR_HANDLE);
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync(error);
            }

            return 2;
        }

        // Before any window, WebView2 or file: an install's start of the overlay can still come with an elevated client's
        // token where no shell runs to start it unelevated, and a log written then would be an elevated write into the
        // user's folder, which a link there sends anywhere. The terminal's error stream is no file.
        if (request is OverlayRequest or SettingsRequest && Elevation.Raised())
        {
            CommandLineOutput.Borrow(STD_HANDLE.STD_ERROR_HANDLE);
            await Console.Error.WriteLineAsync(Elevation.Refusal);
            return 1;
        }

        return request switch
        {
            OverlayRequest overlay => await RunOverlayAsync(overlay.Trace, time),
            SettingsRequest window => await RunWindowAsync(window, time),
            PreviewRequest preview => await Preview.RunAsync(preview, time),
            CpuLoadRequest load => await CpuLoad.RunAsync(load.Seconds, time),
            ElevatedRequest elevated => await RunElevatedAsync(elevated.Verb, time),
            BuiltInRequest builtIn => await AnswerAsync(builtIn),
            _ => 2,
        };
    }

    /// <summary>
    /// Starts <c>thermi.exe</c> again with <paramref name="arguments"/>, and <c>--trace</c> when this process traces, so
    /// one trace follows a request from one process to the other.
    /// </summary>
    internal static void Start(string exePath, bool trace, params string[] arguments)
    {
        var start = new ProcessStartInfo(exePath);
        foreach (var argument in trace ? arguments.Append(ThermiCommands.TraceOption) : arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var started = Process.Start(start);
    }

    private static async Task<int> RunOverlayAsync(bool trace, TimeProvider time)
    {
        // A second thermi opens the window, or brings it to the front, and starts no second overlay. It lets go of the
        // mutex first, or an overlay started while that window is open would find it and never run.
        var single = new Mutex(initiallyOwned: true, OverlayMutex, out var first);
        if (!first)
        {
            single.Dispose();
            return await RunWindowAsync(new SettingsRequest(0, trace, Pseudo: false), time);
        }

        // Above normal, as the sensor service is: a PC busy on every core is when the overlay must keep painting.
        using (var self = Process.GetCurrentProcess())
        {
            self.PriorityClass = ProcessPriorityClass.AboveNormal;
        }

        using (single)
        {
            using var logs = ThermiLog.Create(ThermiLog.OverlayPath, time, trace);
            return await ThermiLog.RunLoggedAsync(logs.CreateLogger("Thermi.Overlay"), () => RunHostAsync(logs, time, single));
        }
    }

    private static Task<int> RunElevatedAsync(string verb, TimeProvider time)
    {
        var log = new EventLogger(StartSensors.Service);
        return ThermiLog.RunLoggedAsync(log, () => Elevated.RunAsync(verb, ElevatedParts.Windows(log), time));
    }

    private static Task<int> AnswerAsync(BuiltInRequest builtIn)
    {
        CommandLineOutput.Borrow(STD_HANDLE.STD_OUTPUT_HANDLE);
        return builtIn.Parsed.InvokeAsync();
    }

    private static async Task<int> RunHostAsync(ILoggerFactory logs, TimeProvider time, Mutex single)
    {
        var sensors = new SensorClient(time, logs.CreateLogger<SensorClient>(), new SensorPipes(time, logs.CreateLogger<SensorPipes>(), starts: true));
        var feed = new SystemFeed(sensors, time);
        using var stop = new CancellationTokenSource();
        var reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token);
        // No settings file yet is thermi's first start, which its first notice and the values' tooltips speak to.
        var firstStart = !File.Exists(ConfigStore.DefaultPath);
        OverlayHost? running = null;
        sensors.Received += (_, _) => running?.Arrived();
        var updates = new OverlayUpdates(() => Facts(running), () => Close(running), time, logs.CreateLogger<OverlayUpdates>());
        using var ticks = time.CreateTimer(_ => updates.Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var mutex = single;
        try
        {
            // An overlay that closed to hand over to a new version, which did not come up, runs again as it was.
            do
            {
                await RunHostOnceAsync(logs, time, sensors, feed, firstStart, host => running = host);
                firstStart = false;
                if (updates.Leaving is null)
                {
                    break;
                }

                mutex.Dispose();
                if (await updates.HandOverAsync())
                {
                    break;
                }

                // Another overlay took the name while this one was away: it is the one that runs.
                mutex = new Mutex(initiallyOwned: true, OverlayMutex, out var created);
                if (!created)
                {
                    break;
                }
            }
            while (true);
        }
        finally
        {
            mutex.Dispose();
        }

        await stop.CancelAsync();
        await reading;
        return 0;
    }

    private static async Task RunHostOnceAsync(
        ILoggerFactory logs, TimeProvider time, SensorClient sensors, SystemFeed feed, bool firstStart, Action<OverlayHost?> running)
    {
        using var store = new ConfigStore(ConfigStore.DefaultPath, time);
        await using var settings = new Settings(store, time, logs.CreateLogger<Settings>());
        using var host = new OverlayHost(settings, () => feed.Shown, CultureInfo.CurrentCulture, time, logs.CreateLogger<OverlayHost>())
        {
            Service = () => sensors.State,
            FirstStart = firstStart,
            Resumed = sensors.Resume,
        };
        running(host);
        host.Run();
        running(null);
    }

    // Read on the update's timer: a list the window's thread changes meanwhile counts as overlays shown.
    private static SessionFacts Facts(OverlayHost? host)
    {
        bool hidden;
        bool dragging;
        try
        {
            // An overlay with none on screen yet, before its first paint, is no hidden one.
            var overlays = host?.Overlays.ToList() ?? [];
            hidden = overlays.Count > 0 && overlays.TrueForAll(overlay => !overlay.OnScreen);
            dragging = overlays.Exists(overlay => overlay.Dragging);
        }
        catch (InvalidOperationException)
        {
            (hidden, dragging) = (false, true);
        }

        return new SessionFacts(host is null || HostEnvironment.Default.WindowOpen(), dragging, Desktop.SinceInput(), Desktop.Locked(), hidden);
    }

    // As the tray's "Exit thermi": the host closes on its own thread, its settings saved.
    private static void Close(OverlayHost? host)
    {
        if (host is not null)
        {
            PInvoke.PostMessage(host.Hwnd, PInvoke.WM_CLOSE, 0, 0);
        }
    }
    private static async Task<int> RunWindowAsync(SettingsRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.WindowPath, time, request.Trace);
        return await ThermiLog.RunLoggedAsync(
            logs.CreateLogger("Thermi.Ui"),
            () => WindowProcess.RunAsync(request.Overlay, WindowEnvironment.Of(logs, time), request.Pseudo, time, request.Item));
    }
}
