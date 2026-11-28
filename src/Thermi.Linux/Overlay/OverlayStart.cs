using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Core.Translations;
using Thermi.Linux.Agents;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;
using Thermi.Linux.Sensors;

namespace Thermi.Linux.Overlay;

/// <summary>
/// <c>thermi</c> with no command, as its user unit starts it in the person's session: the overlay, until "Exit thermi".
/// Under <c>--trace</c> its events go to the standard error, a line each, so a test waits for what thermi did, and what
/// goes wrong there too. A desktop with nowhere to show the overlays ends it at once, with <see cref="CannotShowCode"/>,
/// and says why in one notice, only the first time; with no display for GTK, it ends with 1, and only logs it, as no
/// desktop is there to tell.
/// </summary>
internal static class OverlayStart
{
    /// <summary>The button of the notice that this desktop shows no overlay, which only closes it.</summary>
    public const string NoOverlayAction = "no-overlay";

    /// <summary>
    /// The exit code of a desktop that cannot show the overlays, which the overlay's unit does not start it again on, as
    /// each start would find the same; systemd names it NOTCONFIGURED. One with no display yet may find one at the next.
    /// </summary>
    public const int CannotShowCode = 6;

    // How long the notice's server has to take it, past which thermi ends without it, as every server answers in far less.
    private static readonly TimeSpan NoticeBound = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(OverlayRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.OverlayPath, time, request.Trace, request.Trace ? Console.Error : null);
        var log = logs.CreateLogger("thermi");
        return await ThermiLog.RunLoggedAsync(log, async () =>
        {
            var kind = DesktopSession.Start();
            FoundSurfaces.Write(kind);
            new UserUnit().EnableOnce(UserUnit.Marker, log);
            // GirCore hands the session's one connection out as one object, which lives as long as the process.
            var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
            if (kind == SurfaceKind.NoDisplay)
            {
                log.NoDisplay();
                return 1;
            }

            if (kind is SurfaceKind.None or SurfaceKind.NoLayerShell)
            {
                log.NoSurfaces(kind);
                await TellNoOverlayAsync(bus, time, log, kind == SurfaceKind.NoLayerShell ? HostText.NoOverlayLayerShell : HostText.NoOverlaySurfaces);
                return CannotShowCode;
            }

            var sensors = new SocketFeed(time, logs.CreateLogger<SocketFeed>());
            using var stop = new CancellationTokenSource();
            var reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token);
            // A first start is one that found no settings, which thermi then writes.
            var firstStart = !File.Exists(ConfigStore.DefaultPath);
            using var store = new ConfigStore(ConfigStore.DefaultPath, time, DesktopLooks.RunsInstalled);
            int code;
            await using (var settings = new Settings(store, time, logs.CreateLogger<Settings>()))
            {
                var trace = new TraceEvents(time, request.Trace ? Console.Error : null);
                using var host = new OverlayHost(bus, settings, sensors, trace, logs.CreateLogger<OverlayHost>(), new ThermiStart(kind, firstStart));
                code = host.Run();
            }

            await stop.CancelAsync();
            await reading;
            return code;
        });
    }

    // Once ever: each start after would tell it again at every sign-in, where nothing changed. A notice left unanswered, or
    // with no notices to show it, is told again at the next start.
    private static async Task TellNoOverlayAsync(Gio.DBusConnection bus, TimeProvider time, ILogger log, HostText why)
    {
        var told = Path.Combine(UserFolders.State, "thermi", "no-overlay-told");
        if (File.Exists(told))
        {
            return;
        }

        var texts = Texts.For(null, CultureInfo.CurrentUICulture);
        using var notices = new DesktopNotices(bus, log);
        var said = notices.Say(texts.Of(HostText.GnomeExtensionTitle), texts.Of(why), (NoOverlayAction, texts.Of(HostText.NoticeGotIt)));
        // The bus answers on GLib's main loop, which no host runs yet: it runs until the answer, or the bound, wakes it.
        var context = GLib.MainContext.Default();
        var ended = Task.WhenAny(said, Task.Delay(NoticeBound, time));
        _ = ended.ContinueWith(_ => context.Wakeup(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        while (!ended.IsCompleted)
        {
            _ = context.Iteration(true);
        }

        if (await ended != said)
        {
            log.NoticeUnanswered(NoticeBound);
            return;
        }

        if (await said is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(told)!);
        await File.WriteAllTextAsync(told, "");
    }
}
