using System.Collections.Concurrent;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Ping;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The ping, from what an echo brings back to what the overlay, the catalog and an agent show, and when echoes go out at
/// all (<c>docs/adr/0020-ping.md</c>); and its host, as the window and an agent edit it.
/// </summary>
public sealed class PingTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly ThermiConfig Pinging = new()
    {
        Overlays = [new OverlayConfig { Items = [new ItemConfig { Id = Metrics.Ping.Id, Metric = Metrics.Ping.Id }] }],
    };

    // What the echo did, as the system's ping says it; then what the item shows and why it has no value. With no ping item
    // nothing is sent, so there is nothing to say but that.
    [Theory] // Rows: each a way an echo ends, the last two with nothing sent
    [InlineData("reply", "23 ms", null)]
    [InlineData("timeout", ReadingFormatter.Missing, NoValue.PingTimedOut)]
    [InlineData("unreachable", ReadingFormatter.Missing, NoValue.PingUnreachable)]
    [InlineData("unknown host", ReadingFormatter.Missing, NoValue.PingUnknownHost)]
    [InlineData("no network", ReadingFormatter.Missing, NoValue.PingNoNetwork)]
    [InlineData("no item", ReadingFormatter.Missing, NoValue.PingOff)]
    public async Task APingShowsItsRoundTripOrWhyItHasNone(string echo, string shown, NoValue? why)
    {
        Func<(IPStatus Status, long RoundTrip)> answer = echo switch
        {
            "reply" => () => (IPStatus.Success, 23),
            "timeout" => () => (IPStatus.TimedOut, 0),
            "unreachable" => () => (IPStatus.DestinationHostUnreachable, 0),
            "unknown host" => () => throw new PingException("", new SocketException((int)SocketError.HostNotFound)),
            _ => () => (IPStatus.Success, 1),
        };
        var fake = new FakeEcho(answer) { Up = echo != "no network" };
        var config = echo == "no item" ? new ThermiConfig() : Pinging;
        var latest = await FirstReplyAsync(fake, config);

        var snapshot = PingSensors.With(TestRecordings.Read(TestRecordings.Admin), latest);
        var input = new TrayInput(snapshot, ServiceState.Running, English, Texts.English, LightTaskbar: false, "");
        var scene = ThermiMark.Overlay(Pinging, 0, input, windowOpenedOnce: true);
        var tooltip = ValueTooltips.Text(Pinging, 0, input, scene.Tooltips[0]);
        var device = SensorCatalog.Build(snapshot).Single(each => each.Sensors.Any(row => row.Sensor.Id == PingSensors.Id));
        var read = Assert.Single(AgentReads.Read([snapshot], Pinging, [Metrics.Ping.Id]).Values);

        Assert.Equal(("PING", shown), (scene.Items[0].Label, scene.Items[0].Value));
        Assert.Equal((DeviceKind.Network, why), (device.Kind, device.Sensors.Single(row => row.Sensor.Id == PingSensors.Id).Why));
        Assert.Equal((why is null ? 23d : null, "ms", why), (read.Value, read.Unit, read.Why));
        Assert.Equal(why is not null, why is { } reason && tooltip.Contains(Texts.English.Of(reason), StringComparison.Ordinal));
    }

    // Nothing leaves the PC until an item reads the ping; then one echo a second goes to the host the settings name, or to
    // 1.1.1.1, and none once its process stops following settings.
    [Fact]
    public async Task EchoesGoOutOnlyWhileAnItemReadsThePing()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeEcho(() => (IPStatus.Success, 12));
        var reader = new PingReader(fake, time, NullLogger.Instance);
        var config = new ThermiConfig();
        var reads = 0;
        reader.Follow(() =>
        {
            Interlocked.Increment(ref reads);
            return config;
        });
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => reader.RunAsync(stop.Token), TestContext.Current.CancellationToken);

        for (var second = 1; second <= 3; second++)
        {
            await UntilAsync(() => Volatile.Read(ref reads) == second);
            time.Advance(PingReader.Period);
        }

        await UntilAsync(() => Volatile.Read(ref reads) == 4);
        Assert.Empty(fake.Hosts);
        config = Pinging;
        time.Advance(PingReader.Period);
        await UntilAsync(() => fake.Hosts.Count == 1);
        config = Pinging with { PingHost = "example.com" };
        time.Advance(PingReader.Period);
        await UntilAsync(() => fake.Hosts.Count == 2);
        reader.Follow(null);
        time.Advance(PingReader.Period);
        await UntilAsync(() => reader.Latest is null);
        time.Advance(PingReader.Period);
        await stop.CancelAsync();
        await running;

        Assert.Equal([PingSensors.DefaultHost, "example.com"], fake.Hosts);
        Assert.Equal(6, Volatile.Read(ref reads));
        Assert.All(fake.Timeouts, timeout => Assert.Equal(PingReader.Period, timeout));
    }

    // The host typed, as the window and thermi-cli's edit send it: a name or an address is kept trimmed, empty goes back to
    // 1.1.1.1, anything else is refused and changes nothing, and undo takes a change back.
    [Theory] // Rows: each a kind of host, and what is none
    [InlineData("\"192.168.1.1\"", "192.168.1.1")]
    [InlineData("\" eu.game-server.example \"", "eu.game-server.example")]
    [InlineData("\"2606:4700:4700::1111\"", "2606:4700:4700::1111")]
    [InlineData("null", null)]
    [InlineData("\"not a host!\"", "refused")]
    [InlineData("too long", "refused")]
    public void ThePingsHostIsANameOrAnAddress(string typed, string? kept)
    {
        var before = Pinging with { PingHost = "10.0.0.1" };
        var history = new UndoHistory();
        // A name of short labels, each fine, two characters past what DNS allows in all.
        var value = typed == "too long" ? $"\"{string.Concat(Enumerable.Repeat("a.", 127))}a\"" : typed;

        var edit = AgentEdits.Of(
            $$"""{"kind":"thermi","field":"pingHost","value":{{value}}}""",
            TestRecordings.Read(TestRecordings.Admin),
            Texts.English,
            () => new HashSet<int>())(before);

        if (kept == "refused")
        {
            Assert.Equal(((Refusal?)Refusal.PingHost, Edited.File(before)), (edit.Refused, Edited.File(edit.Config)));
            return;
        }

        history.Record(before, Edited.Done(edit));
        Assert.Equal((kept, kept ?? PingSensors.DefaultHost), (edit.Config.PingHost, PingSensors.HostOf(edit.Config)));
        Assert.Equal(Edited.File(before), Edited.File(history.Undo(edit.Config)!.Config));
    }

    // The reply of the reader's first echo, once its first second has gone by.
    private static async Task<EchoReply?> FirstReplyAsync(FakeEcho echo, ThermiConfig config)
    {
        var time = new FakeTimeProvider();
        var reader = new PingReader(echo, time, NullLogger.Instance);
        var reads = 0;
        reader.Follow(() =>
        {
            Interlocked.Increment(ref reads);
            return config;
        });
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => reader.RunAsync(stop.Token), TestContext.Current.CancellationToken);
        await UntilAsync(() => Volatile.Read(ref reads) == 1);
        time.Advance(PingReader.Period);
        await UntilAsync(() => Volatile.Read(ref reads) == 2);
        var latest = reader.Latest;
        await stop.CancelAsync();
        await running;
        return latest;
    }

    private static async Task UntilAsync(Func<bool> done)
    {
        var waited = 0;
        while (!done())
        {
            Assert.True(++waited < 2500, "The ping's reader never got there.");
            await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
        }
    }

    // The system's ping as the reader sees it, never kinder: what it answers is the row's, and with no network it is never asked.
    private sealed class FakeEcho(Func<(IPStatus Status, long RoundTrip)> answer) : IEcho
    {
        private readonly ConcurrentQueue<(string Host, TimeSpan Timeout)> _sent = new();

        public bool Up { get; init; } = true;

        public IReadOnlyList<string> Hosts => [.. _sent.Select(each => each.Host)];

        public IReadOnlyList<TimeSpan> Timeouts => [.. _sent.Select(each => each.Timeout)];

        public bool NetworkUp() => Up;

        public Task<(IPStatus Status, long RoundTrip)> SendAsync(string host, TimeSpan timeout, CancellationToken stop)
        {
            Assert.True(Up, "An echo went out with no network up.");
            _sent.Enqueue((host, timeout));
            return Task.FromResult(answer());
        }
    }
}
