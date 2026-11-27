using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Core.Translations;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;
using Thermi.Linux.Sensors;

namespace Thermi.Linux.Overlay;

/// <summary>
/// <c>thermi</c> with no command, as its user unit starts it in the person's session: the overlay, until "Exit thermi".
/// Under <c>--trace</c> its events go to the standard error, a line each, so a test waits for what thermi did. A desktop
/// with nowhere to show the overlays ends it at once, with 1, and says why in one notice, only the first time.
/// </summary>
internal static class OverlayStart
{
    /// <summary>The button of the notice that this desktop shows no overlay, which only closes it.</summary>
    public const string NoOverlayAction = "no-overlay";

    public static async Task<int> RunAsync(OverlayRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.OverlayPath, time, request.Trace);
        var log = logs.CreateLogger("thermi");
        return await ThermiLog.RunLoggedAsync(log, async () =>
        {
            var kind = DesktopSession.Start();
            // GirCore hands the session's one connection out as one object, which lives as long as the process.
            var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
            if (kind == SurfaceKind.None)
            {
                log.NoSurfaces();
                await TellNoOverlayAsync(bus);
                return 1;
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

    // Once ever: each start after would tell it again at every sign-in, where nothing changed.
    private static async Task TellNoOverlayAsync(Gio.DBusConnection bus)
    {
        var told = Path.Combine(UserFolders.State, "thermi", "no-overlay-told");
        if (File.Exists(told))
        {
            return;
        }

        var texts = Texts.For(null, CultureInfo.CurrentUICulture);
        using var notices = new DesktopNotices(bus);
        await notices.Say(texts.Of(HostText.GnomeExtensionTitle), texts.Of(HostText.NoOverlaySurfaces), (NoOverlayAction, texts.Of(HostText.NoticeGotIt)));
        Directory.CreateDirectory(Path.GetDirectoryName(told)!);
        await File.WriteAllTextAsync(told, "");
    }
}
