using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Ping;
using Thermi.App.Window;
using Thermi.Core.Pipe;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Tests.Agents;
using static Thermi.Tests.Acceptance.PingTests;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The ping as the processes that read it get it, through <see cref="PingFeed"/>: the settings window, the overlay with the
/// service silent, and an agent's sensors, edit and watch (<c>docs/adr/0020-ping.md</c>).
/// </summary>
public sealed class PingFeedTests
{
    // A snapshot Core failed to read stands, in the window, for the last one it read, the ping on it, which only holds
    // while a snapshot read twice is the same one.
    [Fact]
    public void AWindowThatCannotReadASnapshotShowsTheLastOneItRead()
    {
        var admin = TestRecordings.Read(TestRecordings.Admin);
        var service = new HeldFeed { Latest = admin };
        var feed = new PingFeed(service, new PingReader(Replies(9), new FakeTimeProvider(), NullLogger.Instance));
        feed.Follow(() => Pinging);
        var window = new ReadSensors(feed, NullLogger.Instance);

        var read = window.Take(() => "read");
        service.Latest = new Snapshot([]);
        var unread = window.Take<string>(() => throw new InvalidOperationException("Core cannot read this snapshot."));

        Assert.Equal(("read", null), (read, unread));
        Assert.Equal(admin.Sensors.Count + 1, window.Latest?.Sensors.Count);
    }

    // With no fresh snapshot of the service's, each echo's end is news of its own, so the overlay records the one live
    // reading, and a ping item's graph and statistics follow its value.
    [Fact]
    public async Task WithTheServiceSilentEachEchoIsNews()
    {
        var time = new FakeTimeProvider();
        var feed = new PingFeed(new HeldFeed(), new PingReader(Replies(9), time, NullLogger.Instance));
        feed.Follow(() => Pinging);
        var received = 0;
        feed.Received += (_, _) => Interlocked.Increment(ref received);
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => feed.RunAsync(stop.Token), TestContext.Current.CancellationToken);

        await AdvanceUntilAsync(time, () => Volatile.Read(ref received) >= 3);
        await stop.CancelAsync();
        await running;

        Assert.Equal(9d, feed.Shown.Sensors.Single(sensor => sensor.Id == PingSensors.Id).Value);
    }

    // The ping's sensor, which an agent's sensors list, is one an agent may add an item of, as the window does.
    [Fact]
    public async Task AnAgentAddsAPingItemAsItsSensorsListIt()
    {
        using var rig = new AgentRig { Echo = Replies(9) };
        rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));

        var listed = await rig.RunAsync("sensors");
        var added = await rig.RunAsync("edit", $$"""{"kind":"addItem","overlay":0,"sensor":"{{PingSensors.Id}}"}""");

        Assert.Contains(PingSensors.Id, listed.Out, StringComparison.Ordinal);
        Assert.Equal((0, PingSensors.DefaultHost), (added.Exit, PingSensors.HostOf(rig.Folder.Read())));
    }

    // A watch follows the settings each line reads: no echo before a ping item is added, then echoes to its host.
    [Fact]
    public async Task AWatchPingsAsTheSettingsItReadsSay()
    {
        var echo = Replies(9);
        using var rig = new AgentRig { Echo = echo };
        rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var series = TestRecordings.Series(TestRecordings.CpuLoad).Take(6).ToList();
        rig.Sensors = () =>
        {
            var feed = new RecordedFeed(series, pace: rig.Folder.Clock);
            var arrived = 0;
            feed.Received += (_, _) =>
            {
                if (++arrived == 3)
                {
                    rig.Folder.Write(ThermiConfigJson.Write(Pinging with { PingHost = "example.com" }));
                }
            };
            return feed;
        };

        var end = await rig.RunAsync("watch", Metrics.Ping.Id);

        var lines = end.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!["values"]![0]!).ToList();
        Assert.Equal(("ping-off", 9d), ((string?)lines[0]["why"], (double?)lines[^1]["value"]));
        Assert.NotEmpty(echo.Sent);
        Assert.All(echo.Sent, sent => Assert.Equal("example.com", sent.Host));
    }

    private static FakeEcho Replies(long milliseconds) => new(_ => Task.FromResult((IPStatus.Success, milliseconds)));

    // The service's feed as a test sets it: what it read last, or nothing, as when the service is stopped.
    private sealed class HeldFeed : ISensorFeed
    {
        public event EventHandler? Received;

        public Snapshot? Latest { get; set; }

        public ServiceState State => Latest is null ? ServiceState.Stopped : ServiceState.Running;

        public Task RunAsync(CancellationToken stop)
        {
            Received?.Invoke(this, EventArgs.Empty);
            return Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, stop).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
