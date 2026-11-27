using System.CommandLine;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.App.Window;
using Thermi.Cli.Commands;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Sensors;

namespace Thermi.Linux.Window;

/// <summary>
/// <c>thermi settings</c> on Linux: the window, its page in WebKitGTK, on a thread of its own that GTK keeps. One window
/// exists: GTK's application, by its id on the session's bus, hands a second request's arguments to the first, which
/// comes to the front on the overlay or item asked, and the second ends. Closing the window ends its process, WebKit's
/// own with it.
/// </summary>
internal static class WindowProcess
{
    /// <summary>The window's id on the session's bus and the desktop's, which its launcher's file is named after.</summary>
    public const string Id = "io.github.claudiopi.thermi";

    public static async Task<int> RunAsync(SettingsRequest request, IReadOnlyList<string> args, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.WindowPath, time, request.Trace);
        var log = logs.CreateLogger("Thermi.Ui");
        var sensors = new SocketFeed(time, logs.CreateLogger<SocketFeed>());
        return await ThermiLog.RunLoggedAsync(log, () => ShowAsync(request, args, sensors, time, log));
    }

    // The settings are read before the window opens, for its language and its overlays, and every write that waits goes
    // out once it has closed. The sensors are read only while it is open. GTK registers, runs and ends on one thread.
    private static async Task<int> ShowAsync(SettingsRequest request, IReadOnlyList<string> args, SocketFeed sensors, TimeProvider time, ILogger log)
    {
        WebKit.Module.Initialize();
        using var store = new ConfigStore(ConfigStore.DefaultPath, time, DesktopLooks.RunsInstalled);
        await using var settings = new Settings(store, time, log);
        await settings.SyncAsync();
        using var watcher = settings.Watch(log);
        using var stop = new CancellationTokenSource();
        var reading = Task.CompletedTask;
        var texts = request.Pseudo ? Texts.Pseudo() : Texts.For(settings.Config.Language, CultureInfo.CurrentUICulture);
        var start = new PageStart(request.Overlay, texts, time, sensors, ConfigStore.DefaultPath, log)
        {
            Load = token => CpuLoad.Start(Environment.ProcessPath ?? "thermi", CpuLoad.Arguments((int)Core.Sensors.FanFinder.MaxRun.TotalSeconds), token),
            Pseudo = request.Pseudo,
            Item = request.Item,
            SizeRow = request.SizeRow,
        };
        var code = 0;
        ExceptionDispatchInfo? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Gtk.Application.New(Id, Gio.ApplicationFlags.HandlesCommandLine);
                code = !app.Register(null) || app.GetIsRemote()
                    ? app.Run(["thermi", .. args])
                    : Run(app, start, settings, (args, request.Trace), () => reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token));
            }
#pragma warning disable CA1031 // Not handled: thrown again on the caller's thread, which a thread's own failure would never reach.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.Start();
        thread.Join();
        await stop.CancelAsync();
        await reading.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        failed?.Throw();
        return code;
    }

    // GTK's loop, on the thread that made the window, with the bridge's turns posted to it.
    private static int Run(Gtk.Application app, PageStart start, Settings settings, (IReadOnlyList<string> Args, bool Trace) asked, Action read)
    {
        read();
        SettingsWindow.Serve(WebKit.WebContext.GetDefault());
        var editor = new WindowEditor(settings, Turn.Post, start.Log, start.Time);
        var chart = new ChartHistory(start.Time);
        using var host = new LinuxHost(start.Log);
        using var bridge = new PageBridge(host, start, editor, chart, Turn.Post);
        var trace = asked.Trace ? new TraceEvents(start.Time, Console.Error) : null;
        using var window = new SettingsWindow(app, bridge, host, start.Log, trace, start.Item);
        editor.Changed += (_, _) => window.Theme(editor.Config.WindowTheme);
        window.Theme(editor.Config.WindowTheme);
        var first = true;
        void Arrived(object? sender, EventArgs e)
        {
            Turn.Post(bridge.Arrived);
        }

        // The other process ends once told it is done, which GTK would otherwise leave to a collection of the line.
        app.OnCommandLine += (application, line) =>
        {
            using var command = line.CommandLine;
            if (!first)
            {
                Again(command, bridge, window);
            }

            first = false;
            return 0;
        };
        start.Sensors.Received += Arrived;
        try
        {
            return app.RunWithSynchronizationContext(["thermi", .. asked.Args]);
        }
        finally
        {
            start.Sensors.Received -= Arrived;
            editor.EndGesture();
            editor.Detach();
        }
    }

    private static void Again(Gio.ApplicationCommandLine command, PageBridge bridge, SettingsWindow window)
    {
        var pick = PickOf(command.GetArguments(out _));
        if (pick is not null)
        {
            bridge.Pick(pick);
        }

        window.Raise(pick?.Item);
        command.Done();
    }

    // A second request's overlay and item, as the first one's are the bridge's start; none for the window as it is.
    private static DesktopPickEventArgs? PickOf(string[] line) =>
        ThermiCommandLine.Parse(line[1..], new Dictionary<Command, Func<ParseResult, IThermiRequest>>(), _ => [], out _)
            is SettingsRequest { Overlay: > 0 } again
            ? new DesktopPickEventArgs(again.Overlay - 1, again.Item, again.SizeRow ? SelectHow.Size : SelectHow.DoubleClick)
            : null;
}

/// <summary>Runs an action on GTK's loop, from any thread, as the window's turn: once, at the loop's next idle moment.</summary>
internal static class Turn
{
    public static void Post(Action action) => GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () =>
    {
        action();
        return false;
    });
}
