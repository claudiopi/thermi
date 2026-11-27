using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Linux.Agents;
using Thermi.Linux.Desktop;
using Thermi.Linux.Sensors;

namespace Thermi.Linux.Overlay;

/// <summary>
/// <c>thermi</c> with no command, as its user unit starts it in the person's session: the overlay, until "Exit thermi".
/// Under <c>--trace</c> its events go to the standard error, a line each, so a test waits for what thermi did.
/// </summary>
internal static class OverlayStart
{
    public static async Task<int> RunAsync(OverlayRequest request, TimeProvider time)
    {
        using var logs = ThermiLog.Create(ThermiLog.OverlayPath, time, request.Trace);
        var log = logs.CreateLogger("thermi");
        return await ThermiLog.RunLoggedAsync(log, async () =>
        {
            new UserUnit().EnableOnce(UserUnit.Marker, log);
            // GirCore hands the session's one connection out as one object, which lives as long as the process.
            var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
            var sensors = new SocketFeed(time, logs.CreateLogger<SocketFeed>());
            using var stop = new CancellationTokenSource();
            var reading = Task.Run(() => sensors.RunAsync(stop.Token), stop.Token);
            using var store = new ConfigStore(ConfigStore.DefaultPath, time, DesktopLooks.RunsInstalled);
            int code;
            await using (var settings = new Settings(store, time, logs.CreateLogger<Settings>()))
            {
                var trace = new TraceEvents(time, request.Trace ? Console.Error : null);
                using var host = new OverlayHost(bus, settings, sensors, trace, logs.CreateLogger<OverlayHost>());
                code = host.Run();
            }

            await stop.CancelAsync();
            await reading;
            return code;
        });
    }
}
