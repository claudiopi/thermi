using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Logging;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Windows;
using Windows.Win32.System.Console;

// Windows 10 1607 and later, which every Win32 call thermi makes needs (Thermi.csproj says why it is set here).
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows10.0.14393")]

namespace Thermi;

internal static class Program
{
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

        return request switch
        {
            OverlayRequest overlay => await RunOverlayAsync(overlay.Trace, time),
            SettingsRequest window => await RunWindowAsync(window, time),
            PreviewRequest preview => await Preview.RunAsync(preview, time),
            CpuLoadRequest load => await CpuLoad.RunAsync(load.Seconds, time),
            ElevatedRequest elevated => await Elevated.RunAsync(elevated.Verb, ElevatedParts.Windows, time),
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
        var single = new Mutex(initiallyOwned: true, @"Local\thermi-overlay", out var first);
        if (!first)
        {
            single.Dispose();
            return await RunWindowAsync(new SettingsRequest(0, trace, Pseudo: false), time);
        }

        using (single)
        {
            using var logs = ThermiLog.Create(ThermiLog.DefaultPath, time, trace);
            return await ThermiLog.RunLoggedAsync(logs.CreateLogger("Thermi.Overlay"), () => RunHostAsync(logs, time));
        }
    }

    private static Task<int> AnswerAsync(BuiltInRequest builtIn)
    {
        CommandLineOutput.Borrow(STD_HANDLE.STD_OUTPUT_HANDLE);
        return builtIn.Parsed.InvokeAsync();
    }

    private static async Task<int> RunHostAsync(ILoggerFactory logs, TimeProvider time)
    {
        var sensors = new SensorClient(time, logs.CreateLogger<SensorClient>());
        var feed = new SystemFeed(sensors, time);
        using var stop = new CancellationTokenSource();
        var reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token);
        // No settings file yet is thermi's first start, which its first notice and the values' tooltips speak to.
        var firstStart = !File.Exists(ConfigStore.DefaultPath);
        using var store = new ConfigStore(ConfigStore.DefaultPath, time);
        await using (var settings = new Settings(store, time, logs.CreateLogger<Settings>()))
        {
            using var host = new OverlayHost(settings, () => feed.Latest, CultureInfo.CurrentCulture, time, logs.CreateLogger<OverlayHost>()) { Service = () => sensors.State, FirstStart = firstStart, Resumed = sensors.Resume };
            sensors.Received += (_, _) => host.Arrived();
            host.Run();
        }

        await stop.CancelAsync();
        await reading;
        return 0;
    }

    private static async Task<int> RunWindowAsync(SettingsRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.DefaultPath, time, request.Trace);
        return await ThermiLog.RunLoggedAsync(logs.CreateLogger("Thermi.Ui"), () => WindowProcess.RunAsync(request.Overlay, WindowEnvironment.Of(logs, time), request.Pseudo, time, request.Item));
    }
}
