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
using Thermi.Tests.Agents;

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

    // What the echo did, as the system's ping says it, or a lookup that never answers, or an error no ping names; then what
    // the item shows and why it has no value. Before the first echo ends it waits, and with no ping item nothing is sent.
    [Theory] // Rows: each a way an echo ends, an operand of the status and error tests, the wait, and nothing sent
    [InlineData("reply", "23 ms", null)]
    [InlineData("timed out", ReadingFormatter.Missing, NoValue.PingTimedOut)]
    [InlineData("ttl", ReadingFormatter.Missing, NoValue.PingTimedOut)]
    [InlineData("unreachable", ReadingFormatter.Missing, NoValue.PingUnreachable)]
    [InlineData("host not found", ReadingFormatter.Missing, NoValue.PingUnknownHost)]
    [InlineData("no data", ReadingFormatter.Missing, NoValue.PingUnknownHost)]
    [InlineData("try again", ReadingFormatter.Missing, NoValue.PingUnknownHost)]
    [InlineData("network down", ReadingFormatter.Missing, NoValue.PingNoNetwork)]
    [InlineData("no network", ReadingFormatter.Missing, NoValue.PingNoNetwork)]
    [InlineData("slow lookup", ReadingFormatter.Missing, NoValue.PingTimedOut)]
    [InlineData("throws", ReadingFormatter.Missing, NoValue.PingUnreachable)]
    [InlineData("waiting", ReadingFormatter.Missing, NoValue.PingWaiting)]
    [InlineData("no item", ReadingFormatter.Missing, NoValue.PingOff)]
    public async Task APingShowsItsRoundTripOrWhyItHasNone(string echo, string shown, NoValue? why)
    {
        var fake = new FakeEcho(Answer(echo)) { Up = echo != "no network" };
        var latest = await ReplyAsync(fake, echo == "no item" ? new ThermiConfig() : Pinging, waits: echo != "waiting");

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

    // Nothing leaves the PC until an item reads the ping; then one echo a second at most goes to the host the settings name,
    // or to 1.1.1.1, each waited for a second. An echo cut off as its process stops throws nothing out of the reader, and
    // once none has ended for two seconds the last one no longer reads; none goes out once it stops following settings.
    [Fact]
    public async Task EchoesGoOutOnlyWhileAnItemReadsThePing()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeEcho(_ => Task.FromResult((IPStatus.Success, 12L))) { Clock = time };
        var reader = new PingReader(fake, time, NullLogger.Instance);
        var config = new ThermiConfig();
        reader.Follow(() => config);
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => reader.RunAsync(stop.Token), TestContext.Current.CancellationToken);

        await AdvanceUntilAsync(time, () => time.GetUtcNow() > time.Start.AddSeconds(3));
        Assert.Empty(fake.Sent);
        config = Pinging;
        await AdvanceUntilAsync(time, () => fake.Sent.Count == 2);
        config = Pinging with { PingHost = "example.com" };
        await AdvanceUntilAsync(time, () => fake.Sent.Count == 3 && reader.Latest is { RoundTrip: 12 });
        fake.Hangs = true;
        await AdvanceUntilAsync(time, () => fake.Sent.Count == 4);
        await stop.CancelAsync();
        await running;
        time.Advance(PingReader.Stale + PingReader.Period);
        var stale = reader.Latest;
        reader.Follow(null);

        Assert.Equal([PingSensors.DefaultHost, PingSensors.DefaultHost, "example.com", "example.com"], fake.Sent.Select(each => each.Host));
        Assert.All(fake.Sent, each => Assert.Equal(PingReader.Period, each.Timeout));
        Assert.All(fake.Sent.Zip(fake.Sent.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= PingReader.Period));
        Assert.Equal((PingFailure.Waiting, null), (stale?.Failure, reader.Latest));
    }

    // A one-shot read with a ping item says what its first echo did, here a host that never answers, never that it waits.
    [Fact]
    public async Task AReadSaysWhatThePingsFirstEchoDid()
    {
        var never = new FakeEcho(stop => Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, stop)
            .ContinueWith(_ => (IPStatus.Success, 0L), TaskScheduler.Default));
        using var rig = new AgentRig { Echo = never };
        rig.Folder.Write(ThermiConfigJson.Write(Pinging));

        var end = await rig.RunAsync("read", Metrics.Ping.Id);

        Assert.Equal("ping-timed-out", (string?)end.Json["values"]![0]!["why"]);
    }

    // The host typed, as the window and thermi-cli's edit send it: a name or an address is kept trimmed, empty goes back to
    // 1.1.1.1, anything else is refused and changes nothing, undo takes a change back, and a ping item's chart starts again.
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
        var time = new FakeTimeProvider();
        var chart = new ChartHistory(time);
        chart.Follow(before, _ => null);
        time.Advance(TimeSpan.FromSeconds(1));
        var ping = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.Ping.Id] = new(SensorKind.Latency, 20) };
        chart.Items.Add(before.Overlays[0], ping, before.PingHost);
        var drawn = chart.Of(Metrics.Ping.Id).OfType<double>().Count();
        chart.Follow(edit.Config, _ => null);
        Assert.Equal((1, 0), (drawn, chart.Of(Metrics.Ping.Id).OfType<double>().Count()));
    }

    private static Func<CancellationToken, Task<(IPStatus Status, long RoundTrip)>> Answer(string echo) => echo switch
    {
        "reply" => _ => Task.FromResult((IPStatus.Success, 23L)),
        "timed out" => _ => Task.FromResult((IPStatus.TimedOut, 0L)),
        "ttl" => _ => Task.FromResult((IPStatus.TtlExpired, 0L)),
        "unreachable" => _ => Task.FromResult((IPStatus.DestinationHostUnreachable, 0L)),
        "host not found" => _ => throw Failed(SocketError.HostNotFound),
        "no data" => _ => throw Failed(SocketError.NoData),
        "try again" => _ => throw Failed(SocketError.TryAgain),
        "network down" => _ => throw Failed(SocketError.NetworkDown),
        "throws" => _ => throw new InvalidOperationException("No ping names this."),
        // A resolver that answers nothing, as a dead DNS server, for longer than a second, whatever it is told.
        "slow lookup" or "waiting" => _ => Task.Delay(TimeSpan.FromSeconds(10), TimeProvider.System, CancellationToken.None)
            .ContinueWith(_ => (IPStatus.Success, 1L), TaskScheduler.Default),
        _ => _ => Task.FromResult((IPStatus.Success, 1L)),
    };

    private static PingException Failed(SocketError error) => new("", new SocketException((int)error));

    // The reader's reply once its first echo ended, or with waits false, at once.
    private static async Task<EchoReply?> ReplyAsync(FakeEcho echo, ThermiConfig config, bool waits)
    {
        var time = new FakeTimeProvider();
        var reader = new PingReader(echo, time, NullLogger.Instance);
        var answered = 0;
        reader.Answered += (_, _) => Interlocked.Increment(ref answered);
        reader.Follow(() => config);
        using var stop = new CancellationTokenSource();
        var running = Task.Run(() => reader.RunAsync(stop.Token), TestContext.Current.CancellationToken);
        if (waits)
        {
            await AdvanceUntilAsync(time, () => Volatile.Read(ref answered) > 0 || time.GetUtcNow() > time.Start.AddSeconds(3));
        }

        var latest = reader.Latest;
        await stop.CancelAsync();
        await running;
        return latest;
    }

    // Small steps of the fake clock, each let through to the reader, so it is never moved past a wait the reader has yet to start.
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> done)
    {
        var waited = 0;
        while (!done())
        {
            Assert.True(++waited < 2500, "The ping's reader never got there.");
            time.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
        }
    }

    // The system's ping as the reader sees it, never kinder: what it answers is the row's, with no network it is never asked,
    // and once it hangs, it answers only by failing as its echo is cut off.
    internal sealed class FakeEcho(Func<CancellationToken, Task<(IPStatus Status, long RoundTrip)>> answer) : IEcho
    {
        private readonly ConcurrentQueue<(string Host, TimeSpan Timeout, DateTimeOffset At)> _sent = new();

        public bool Up { get; init; } = true;

        public TimeProvider Clock { get; init; } = TimeProvider.System;

        public volatile bool Hangs;

        public IReadOnlyList<(string Host, TimeSpan Timeout, DateTimeOffset At)> Sent => [.. _sent];

        public bool NetworkUp() => Up;

        public async Task<(IPStatus Status, long RoundTrip)> SendAsync(string host, TimeSpan timeout, CancellationToken stop)
        {
            Assert.True(Up, "An echo went out with no network up.");
            _sent.Enqueue((host, timeout, Clock.GetUtcNow()));
            if (Hangs)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, stop).ContinueWith(_ => { }, TaskScheduler.Default);
                throw new PingException("", new OperationCanceledException(stop));
            }

            return await answer(stop);
        }
    }
}
