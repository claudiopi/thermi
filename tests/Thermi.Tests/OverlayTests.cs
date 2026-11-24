using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests;

/// <summary>From a recorded snapshot to what the overlay shows: the path every reading takes.</summary>
public sealed class OverlayTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly Dictionary<string, string> NoSources = new(StringComparer.Ordinal);

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    [Fact]
    public void ShowsTheDefaultOverlayFromARecording()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(Desktop, NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("CPU", "77 °C", Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id },
                new OverlayItem(null, "1,087 RPM", Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id },
                new OverlayItem(null, "9 %", Level.Normal, "100 %") { Id = Metrics.CpuLoad.Id },
                new OverlayItem(null, "71 W", Level.Normal, "888 W") { Id = Metrics.CpuPower.Id },
                new OverlayItem("GPU", "3 %", Level.Normal, "100 %") { Id = Metrics.GpuLoad.Id },
                new OverlayItem("RAM", "27.2 GB", Level.Normal, "888.8 GB") { Id = Metrics.RamUsed.Id },
            ],
            items);
    }

    [Fact]
    public void WithoutAdminRightsTheCpuShowsAsMissingNotAsZero()
    {
        var recording = TestRecordings.Read(TestRecordings.NoAdmin);

        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(recording, NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("CPU", ReadingFormatter.Missing, Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id },
                new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id },
                new OverlayItem(null, "10 %", Level.Normal, "100 %") { Id = Metrics.CpuLoad.Id },
                new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "888 W") { Id = Metrics.CpuPower.Id },
                new OverlayItem("GPU", "3 %", Level.Normal, "100 %") { Id = Metrics.GpuLoad.Id },
                new OverlayItem("RAM", "27.2 GB", Level.Normal, "888.8 GB") { Id = Metrics.RamUsed.Id },
            ],
            items);
    }

    // A sensor that is gone, or named for a metric of another kind, leaves the metric missing: never another sensor's
    // value, nor one in the wrong unit. Without a metric, every metric is.
    [Theory]
    [InlineData("the desktop without Memory Used", null, null, "ram.used")]
    [InlineData("the desktop", "cpu.temperature", "/lpc/it8689e/0/fan/0", "cpu.temperature")]
    [InlineData("the desktop", "cpu.temperature", "/amdcpu/9/temperature/9", "cpu.temperature")]
    [InlineData("a fan's control alone", "fan.cpu", "/lpc/it8689e/0/control/0", "fan.cpu.duty")]
    [InlineData("nothing", null, null, null)]
    public void AMetricWithoutItsSensorIsMissingNeverGuessed(string snapshot, string? named, string? sensor, string? metric)
    {
        var read = snapshot switch
        {
            "the desktop" => Desktop,
            "the desktop without Memory Used" => new Snapshot([.. Desktop.Sensors.Where(each => each.Name != "Memory Used")]),
            "a fan's control alone" => new Snapshot([new RawSensor("/lpc/it8689e/0/control/0", "SuperIO", "ITE IT8689E", "Control", "Fan #1", 42)]),
            _ => new Snapshot([]),
        };
        var sources = named is null ? NoSources : new Dictionary<string, string>(StringComparer.Ordinal) { [named] = sensor! };

        var readings = SensorMap.Read(read, sources);

        Assert.All(metric is null ? Metrics.All.Select(each => each.Id) : [metric], id => Assert.Null(readings[id].Value));
    }

    [Fact]
    public void PrefersTheDiscreteGpuOverTheIntegratedOne()
    {
        var readings = SensorMap.Read(Desktop, NoSources);

        Assert.Equal(3.0, readings[Metrics.GpuLoad.Id].Value);
        Assert.Equal(41.9297, Math.Round(readings[Metrics.GpuTemperature.Id].Value ?? 0, 4));
    }

    [Fact]
    public void TheConfigCanNameTheSensorBehindAMetric()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Metrics.GpuLoad.Id] = "/gpu-amd/0/load/0",
            [Metrics.CpuFan.Id] = "/lpc/it8689e/0/fan/1",
        };

        var desktop = new Snapshot([.. Desktop.Sensors.Select(sensor => sensor.Id == "/lpc/it8689e/0/control/1" ? sensor with { Value = 40 } : sensor)]);

        var readings = SensorMap.Read(desktop, sources);

        Assert.Equal(0.0, readings[Metrics.GpuLoad.Id].Value);
        Assert.Equal(0.0, readings[Metrics.CpuFan.Id].Value);
        Assert.Equal(40.0, readings[Metrics.CpuFanDuty.Id].Value);
    }

    [Fact]
    public void ItemsThatKeepTheSameMetricAsAutoEachShowTheirOwnSensor()
    {
        const string Ccd1 = "/amdcpu/0/temperature/3";
        const string Ccd2 = "/amdcpu/0/temperature/4";
        var ccd2Meter = new MeterConfig { Sensor = Ccd2, Auto = Metrics.CpuTemperature.Id };
        var overlay = new OverlayConfig
        {
            Items =
            [
                new ItemConfig { Id = "ccd1", Sensor = Ccd1, Auto = Metrics.CpuTemperature.Id, Label = "CCD1" },
                new ItemConfig { Id = "ccd2", Sensor = Ccd2, Auto = Metrics.CpuTemperature.Id, Label = "CCD2", Meter = ccd2Meter, Threshold = new Threshold(40, 60) },
                new ItemConfig { Id = "load", Metric = Metrics.CpuLoad.Id, Label = "LOAD", Meter = ccd2Meter, Threshold = new Threshold(40, 60) },
            ],
        };

        var items = OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), English);

        Assert.Equal(["72 °C", "47 °C", "9 %"], items.Select(item => item.Value));

        Assert.Equal([Level.Normal, Level.Warning, Level.Warning], items.Select(item => item.Level));
    }

    [Fact]
    public void WithoutTheSensorHostEveryValueIsMissingAndNoMeterIsLit()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), new Dictionary<string, Reading>(StringComparer.Ordinal), English);

        Assert.All(items, item => Assert.Equal(ReadingFormatter.Missing, item.Value));

        Assert.All(items, item => Assert.Equal(Level.Normal, item.Level));
    }

    [Fact]
    public void TheMeterColorsByItsOwnThreshold()
    {
        var hot = new Dictionary<string, Reading>(StringComparer.Ordinal)
        {
            [Metrics.CpuFan.Id] = new(SensorKind.Fan, 2400),
            [Metrics.CpuFanDuty.Id] = new(SensorKind.Control, 90),
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 96),
            [Metrics.CpuLoad.Id] = new(SensorKind.Load, 140),
        };

        var items = OverlayLayout.Build(new OverlayConfig(), hot, English);

        Assert.Equal(Level.Normal, items[1].Level);
        Assert.Equal(Level.Critical, items[0].Level);

    }

    [Fact]
    public void SkipsItemsForMetricsThisVersionDoesNotKnow()
    {
        var config = new OverlayConfig { Items = [new ItemConfig { Metric = "gpu.hotspot" }, new ItemConfig { Metric = Metrics.RamLoad.Id }] };

        var items = OverlayLayout.Build(config, SensorMap.Read(Desktop, NoSources), English);

        Assert.Equal([new OverlayItem("RAM", "44 %", Level.Normal, "100 %")], items);
    }

    [Fact]
    public void AnItemWithoutAThresholdIsNeverAlarming()
    {
        var config = new OverlayConfig { Items = [new ItemConfig { Metric = Metrics.CpuLoad.Id, Meter = new MeterConfig { Metric = Metrics.CpuLoad.Id } }] };
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuLoad.Id] = new(SensorKind.Load, 99) };

        Assert.Equal(Level.Normal, OverlayLayout.Build(config, readings, English)[0].Level);
    }
}
