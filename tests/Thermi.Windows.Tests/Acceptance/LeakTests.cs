extern alias sensors;

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// Nothing leaks: handles and memory come back after thousands of rounds.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class LeakTests : IDisposable
{
    private const int WarmUp = 100;
    private const int Rounds = 10_000;
    private const long HeapSlack = 1024 * 1024;
    private const int HandleSlack = 25;
    private const int HandlesPerThread = 3;
    private const int TrayIcons = 3;

    // Rounds of 100 ms: the hottest item is critical for 12 s of each 12.8 s, long enough for "Notify me" and a glow each time.
    private const int Episode = 128;
    private const int Critical = 120;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SettingsFolder _folder = new();
    private readonly string _windowMutex = HostRun.WindowMutex();
    private readonly FakeTray _tray = new(record: false);
    private int _round;
    private bool _warmer;
    private bool _stopped;

    public void Dispose() => _folder.Dispose();

    // Two overlays, one fading, outlined, vertical and with a graph and a maximum, so every way of painting is in each
    // round, and three items, past the maximum, with a tray icon each, whose values change every round. The service stops
    // every other round, so the stopped pill takes the items' place and their tooltips go and come back. The process's
    // handles count from halfway, since the thread pool grows its threads, each with its handles, once along the way.
    [Fact]
    public async Task NothingLeaksAfterTenThousandRounds()
    {
        _folder.Write(ThermiConfigJson.Write(EveryWayOfPainting()));
        var settings = await _folder.LoadedAsync();
        (uint Gdi, uint User, long Heap, int Handles) warm = default, half = default, done = default;
        var shown = 0;

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => _warmer ? HostRun.Warmer : HostRun.Recorded, CultureInfo.GetCultureInfo("en-US"), _folder.Clock, _folder.Log, _tray.Environment(_windowMutex))
            {
                Service = () => _stopped ? ServiceState.Stopped : ServiceState.Running,
            };
            host.Start();
            Run(host, settings, WarmUp);
            warm = Measure();
            Run(host, settings, Rounds / 2);
            half = Measure();
            Run(host, settings, Rounds / 2);
            done = Measure();
            shown = _tray.Shown.Count;
        });

        // thermi's own icon always shows, besides the items'.
        Assert.Equal((TrayIcons + 1, 0), (shown, _tray.Shown.Count));
        Assert.True(_tray.Redrawn >= TrayIcons * Rounds);
        Assert.Equal((warm.Gdi, warm.User), (done.Gdi, done.User));
        Assert.InRange(done.Heap - warm.Heap, long.MinValue, HeapSlack);
        Assert.InRange(done.Handles - half.Handles, int.MinValue, HandleSlack);
    }

    // Each client connects and leaves; the service lets go of everything it took for one, the pipe and what it made the
    // pipe with, at once, without waiting for the garbage collector to.
    [Fact]
    public async Task TheSensorServiceKeepsNothingOfAThousandClients()
    {
        var clock = new FakeTimeProvider();
        var pipe = $"thermi-test-{Guid.NewGuid():N}";
        using var stop = new CancellationTokenSource();
        using var server = new SensorPipeServer(() => HostRun.Recorded, clock, pipe);
        var running = Task.Run(() => server.RunAsync(stop.Token), CancellationToken.None);

        await ComeAndGoAsync(pipe, clock, WarmUp, running);
        var warm = HandlesBesideThreads();
        await ComeAndGoAsync(pipe, clock, 1000, running);
        var done = HandlesBesideThreads();
        await stop.CancelAsync();
        await running.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.InRange(done - warm, int.MinValue, HandleSlack);
    }

    private static ThermiConfig EveryWayOfPainting()
    {
        var config = OverlayEdits.Add(new ThermiConfig()).Config;
        config = PowerEdits.SetGraph(config, "cpu.load", graph: true).Config;
        config = PowerEdits.SetStat(config, "cpu.temperature", ItemStat.Max).Config;
        var hottest = SensorMap.Read(HostRun.Recorded, config.Overlays[0])["cpu.temperature"].Value!.Value;
        config = config with
        {
            Overlays =
            [
                config.Overlays[0] with
                {
                    Items = [.. config.Overlays[0].Items.Select((item, at) => item with
                    {
                        Tray = at is >= 2 and < 2 + TrayIcons,
                        Notify = item.Id == "cpu.temperature",
                        Threshold = item.Id == "cpu.temperature" ? new Threshold(hottest - 10, hottest + 1.5) : item.Threshold,
                    })],
                },
                .. config.Overlays.Skip(1),
            ],
        };
        return StyleEdits.Restyle(config, 1, style => style with { Opacity = new OpacityConfig(0.5, 0.2), Effect = TextEffect.Outline, Orientation = Orientation.Vertical }).Config;
    }

    // Clients one after another; past the service's four, each waits for a slot, which frees at the service's next write.
    private static async Task ComeAndGoAsync(string pipe, FakeTimeProvider clock, int clients, Task running)
    {
        for (var client = 0; client < clients; client++)
        {
            while (true)
            {
                await using var connected = new NamedPipeClientStream(".", pipe, PipeDirection.In, PipeOptions.Asynchronous);
                try
                {
                    await connected.ConnectAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
                    break;
                }
                catch (TimeoutException)
                {
                    Assert.False(running.IsCompleted, $"The service ended: {running.Exception}");
                    clock.Advance(TimeSpan.FromSeconds(1));
                }
            }
        }
    }

    // A thread holds 3 handles of its own, measured, and under a loaded PC the thread pool adds threads while the clients
    // come and go, and nine new ones alone are past the slack. Left out, what remains is what the service holds.
    private static int HandlesBesideThreads()
    {
        using var self = Process.GetCurrentProcess();
        return self.HandleCount - (HandlesPerThread * self.Threads.Count);
    }

    private static int Handles()
    {
        using var self = Process.GetCurrentProcess();
        return self.HandleCount;
    }

    // Each round paints both overlays with a fresh snapshot, which redraws the three tray icons, reloads the settings, and
    // sees the window's mutex appear and vanish with a "show".
    private void Run(OverlayHost host, Settings settings, int rounds)
    {
        for (var round = 0; round < rounds; round++)
        {
            _folder.Clock.Advance(Settings.Interval);
            _warmer = _round++ % Episode < Critical;
            _stopped = !_stopped;
            host.Arrived();
            var reload = settings.SyncAsync();
            Assert.True(reload.IsCompletedSuccessfully);
            using (new Mutex(initiallyOwned: false, _windowMutex))
            {
                Assert.True(OverlayHost.AskToShow());
            }

            HostRun.Pump(host);
        }
    }

#pragma warning disable S1215 // The heap is measured after a full collection, as the spec asks.
    private static (uint Gdi, uint User, long Heap, int Handles) Measure()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var (gdi, user) = TestWindows.Handles();
        return (gdi, user, GC.GetTotalMemory(forceFullCollection: true), Handles());
    }
#pragma warning restore S1215
}
