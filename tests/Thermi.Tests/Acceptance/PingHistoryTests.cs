using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Look;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What an item keeps of the ping: its minute, its statistics and the ten minutes of what it warns by, as the overlay
/// records them each second, and what a new host starts again (<c>docs/adr/0020-ping.md</c>).
/// </summary>
public sealed class PingHistoryTests
{
    private static readonly ItemConfig PingItem = new() { Id = Metrics.Ping.Id, Metric = Metrics.Ping.Id };

    private static readonly Dictionary<string, Reading> Read = new(StringComparer.Ordinal)
    {
        [Metrics.Ping.Id] = new(SensorKind.Latency, 20),
        [Metrics.CpuLoad.Id] = new(SensorKind.Load, 50),
        [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 60),
    };

    // A new host is another reading for what reads the ping, and for nothing else: an item of the ping starts its minute
    // and its meter again, one that only warns by the ping its meter, and a ping item that warns by another reading only
    // its own minute. Then the minute's seconds and the meter's kept.
    [Theory] // Rows: an item of the ping, one that warns by it, and a ping item that warns by another reading
    [InlineData("ping", 0, 0)]
    [InlineData("load by ping", 2, 0)]
    [InlineData("ping by temperature", 0, 2)]
    public void ANewHostStartsAgainOnlyWhatReadsThePing(string item, int minute, int meter)
    {
        var of = item switch
        {
            "ping" => PingItem,
            "load by ping" => new ItemConfig { Id = "load", Metric = Metrics.CpuLoad.Id, WarnBy = new() { Metric = Metrics.Ping.Id } },
            _ => PingItem with { WarnBy = new() { Metric = Metrics.CpuTemperature.Id } },
        };
        var overlay = new OverlayConfig { Items = [of] };
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        for (var second = 0; second < 2; second++)
        {
            history.Add(overlay, Read, "a.example");
            time.Advance(TimeSpan.FromSeconds(1));
        }

        history.Follow(overlay.Items, "b.example");

        var sent = history.Sent(new ThermiConfig { Overlays = [overlay] }).Items.SingleOrDefault();
        Assert.Equal((minute, meter), (sent?.Values.Count(value => !double.IsNaN(value)) ?? 0, history.TenMinutes(of.Id).Count(value => value is not null)));
    }

    // With the service stopped the ping still reads each second, and the overlay records every item then: one that read
    // nothing that second counts no smoothed value of the seconds before into its minimum, maximum and average.
    [Fact]
    public void AnItemThatReadNothingCountsNothing()
    {
        var load = new ItemConfig { Id = "load", Metric = Metrics.CpuLoad.Id, Smooth = 5, Stat = ItemStat.Average };
        var overlay = new OverlayConfig { Items = [load, PingItem] };
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        foreach (var value in new double?[] { 10, 90, null, null, null })
        {
            var read = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.Ping.Id] = new(SensorKind.Latency, 20) };
            if (value is { } loaded)
            {
                read[Metrics.CpuLoad.Id] = new(SensorKind.Load, loaded);
            }

            history.Add(overlay, read, PingSensors.DefaultHost);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(30d, history.Shown(load, new Reading(SensorKind.Load, 0))?.Value);
    }
}
