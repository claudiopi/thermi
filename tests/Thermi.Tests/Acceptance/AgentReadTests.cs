using System.Globalization;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>What an agent reads is what the overlay shows, from the same snapshots and settings, never a value it made up.</summary>
public sealed class AgentReadTests
{
    [Theory]
    [InlineData(TestRecordings.Admin)]
    [InlineData(TestRecordings.NoAdmin)]
    [InlineData(TestRecordings.IntelAdmin)]
    [InlineData(TestRecordings.NetDisk)]
    public void ReadGivesWhatTheOverlayShows(string recording)
    {
        var snapshot = TestRecordings.Read(recording);
        // The last fan the service reads, which the automatic choice never takes first, as the CPU's.
        var fan = snapshot.Sensors.Last(sensor => sensor.Type == "Fan").Id;
        var config = Done(ItemEdits.UseCpuFan(new ThermiConfig(), 0, snapshot, fan));

        var all = AgentReads.Read([snapshot], config, []);

        Assert.Empty(all.Unknown);
        Assert.Equal(
            [.. Metrics.All.Select(metric => metric.Id), .. config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id)],
            all.Values.Select(value => value.Id));
        Assert.All(all.Values, value => Assert.False(string.IsNullOrEmpty(value.Unit), $"{value.Id} has no unit."));
        Assert.All(all.Values, value => Assert.True(value.Value is not null || value.Why is not null, $"{value.Id} has no value and no why."));
        Assert.Equal(fan, Assert.Single(all.Values, value => value is { Id: "fan.cpu", Source: ValueSource.Metric }).Sensor);
        if (SensorMap.DriverMayBeMissing(snapshot))
        {
            var temperature = Assert.Single(all.Values, value => value is { Id: "cpu.temperature", Source: ValueSource.Metric });
            Assert.Null(temperature.Value);
            Assert.Equal(NoValue.DriverMayBeMissing, temperature.Why);
        }

        var shown = OverlayLayout.Build(config.Overlays[0], SensorMap.Read(snapshot, config.Overlays[0]), CultureInfo.InvariantCulture, windowOpen: true);
        Assert.All(shown, item =>
        {
            var read = Assert.Single(all.Values, value => value.Id == item.Id && value.Source == ValueSource.Item);
            Assert.Equal((item.Value, item.Level), (read.Shown, read.Level));
        });

        // An item's id may be a metric's, as the default items' are, so an item is asked for by "item:" and its id.
        var some = AgentReads.Read([snapshot], config, [Metrics.GpuLoad.Id, snapshot.Sensors[0].Id, AgentReads.ItemPrefix + shown[0].Id, "no.such"]);

        Assert.Equal(
            [(Metrics.GpuLoad.Id, ValueSource.Metric), (snapshot.Sensors[0].Id, ValueSource.Sensor), (shown[0].Id, ValueSource.Item)],
            some.Values.Select(value => (value.Id, value.Source)));
        Assert.Equal(["no.such"], some.Unknown);
    }
}
