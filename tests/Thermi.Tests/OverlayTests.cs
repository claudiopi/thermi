using System.Globalization;
using Thermi.Core;

namespace Thermi.Tests;

/// <summary>From a recorded snapshot to what the overlay shows: the path every reading takes.</summary>
public sealed class OverlayTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly Dictionary<string, string> NoSources = new(StringComparer.Ordinal);

    private static Snapshot Recording(string name) =>
        SnapshotJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recordings", name)).ReplaceLineEndings(""))
        ?? throw new InvalidOperationException($"{name} is not a valid snapshot.");

    private static Snapshot Desktop => Recording("ryzen-9950x-rtx-5060-ti-admin.json");

    // Sensors report single-precision values, so fills carry float noise.
    private static List<OverlayItem> Rounded(IEnumerable<OverlayItem> items) =>
        [.. items.Select(item => item with { Fill = item.Fill is { } fill ? Math.Round(fill, 4) : null })];

    [Fact]
    public void ShowsTheDefaultOverlayFromARecording()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(Desktop, NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("FAN", "1,087 RPM", 0.64, Level.Warning, "8,888 RPM"),
                new OverlayItem("CPU", "77 °C", 0.7663, Level.Warning, "100 °C"),
                new OverlayItem(null, "9 %", 0.0879, Level.Normal, "100 %"),
                new OverlayItem(null, "71 W", null, Level.Normal, "888 W"),
                new OverlayItem("GPU", "3 %", 0.03, Level.Normal, "100 %"),
                new OverlayItem("RAM", "27.2 GB", 0.4412, Level.Normal, "888.8 GB"),
            ],
            Rounded(items));
    }

    [Fact]
    public void WithoutAdminRightsTheCpuShowsAsMissingNotAsZero()
    {
        var recording = Recording("ryzen-9950x-rtx-5060-ti-no-admin.json");

        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(recording, NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("FAN", ReadingFormatter.Missing, 0, Level.Normal, "8,888 RPM"),
                new OverlayItem("CPU", ReadingFormatter.Missing, 0, Level.Normal, "100 °C"),
                new OverlayItem(null, "10 %", 0.098, Level.Normal, "100 %"),
                new OverlayItem(null, ReadingFormatter.Missing, null, Level.Normal, "888 W"),
                new OverlayItem("GPU", "3 %", 0.03, Level.Normal, "100 %"),
                new OverlayItem("RAM", "27.2 GB", 0.4405, Level.Normal, "888.8 GB"),
            ],
            Rounded(items));
    }

    [Fact]
    public void EachLabelStartsItsOwnPill()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(Desktop, NoSources), English);

        var groups = OverlayLayout.Groups(items);

        Assert.Equal(["FAN", "CPU", "GPU", "RAM"], groups.Select(group => group[0].Label));
        Assert.Equal([1, 3, 1, 1], groups.Select(group => group.Count));
    }

    [Fact]
    public void RamUsedIsNeverGuessedFromAnotherSize()
    {
        var withoutUsed = new Snapshot([.. Desktop.Sensors.Where(sensor => sensor.Name != "Memory Used")]);

        var readings = SensorMap.Read(withoutUsed, NoSources);

        Assert.Null(readings[Metrics.RamUsed.Id].Value);
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
    public void ANamedSensorOfAnotherKindShowsAsMissingNotWithTheWrongUnit()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { [Metrics.CpuTemperature.Id] = "/lpc/it8689e/0/fan/0" };

        var readings = SensorMap.Read(Desktop, sources);

        Assert.Null(readings[Metrics.CpuTemperature.Id].Value);
    }

    [Fact]
    public void ANamedSensorThatIsGoneShowsAsMissing()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal) { [Metrics.CpuTemperature.Id] = "/amdcpu/9/temperature/9" };

        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(Desktop, sources), English);

        Assert.Equal(new OverlayItem("CPU", ReadingFormatter.Missing, 0, Level.Normal, "100 °C"), items[1]);
    }

    [Fact]
    public void WithoutTheSensorHostEveryValueIsMissingAndNoMeterIsLit()
    {
        var items = OverlayLayout.Build(new OverlayConfig(), new Dictionary<string, Reading>(StringComparer.Ordinal), English);

        Assert.All(items, item => Assert.Equal(ReadingFormatter.Missing, item.Value));
        Assert.All(items, item => Assert.True(item.Fill is null or 0));
        Assert.All(items, item => Assert.Equal(Level.Normal, item.Level));
    }

    [Fact]
    public void AnEmptySnapshotLeavesEveryMetricMissing()
    {
        var readings = SensorMap.Read(new Snapshot([]), NoSources);

        Assert.All(Metrics.All, metric => Assert.Null(readings[metric.Id].Value));
    }

    [Fact]
    public void TheMeterColorsByItsOwnThreshold()
    {
        var hot = new Dictionary<string, Reading>(StringComparer.Ordinal)
        {
            [Metrics.CpuFan.Id] = new(SensorKind.Fan, 2400),
            [Metrics.CpuFanDuty.Id] = new(SensorKind.Control, 90),
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 76),
            [Metrics.CpuLoad.Id] = new(SensorKind.Load, 140),
        };

        var items = OverlayLayout.Build(new OverlayConfig(), hot, English);

        Assert.Equal(Level.Critical, items[0].Level);
        Assert.Equal(Level.Warning, items[1].Level);
        Assert.Equal(1.0, items[2].Fill);
    }

    [Fact]
    public void SkipsItemsForMetricsThisVersionDoesNotKnow()
    {
        var config = new OverlayConfig { Items = [new ItemConfig("gpu.hotspot"), new ItemConfig(Metrics.RamLoad.Id)] };

        var items = OverlayLayout.Build(config, SensorMap.Read(Desktop, NoSources), English);

        Assert.Equal([new OverlayItem("RAM", "44 %", null, Level.Normal, "100 %")], items);
    }

    [Fact]
    public void AnItemWithoutAThresholdIsNeverAlarming()
    {
        var config = new OverlayConfig { Items = [new ItemConfig(Metrics.CpuLoad.Id, Meter: Metrics.CpuLoad.Id)] };
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuLoad.Id] = new(SensorKind.Load, 99) };

        Assert.Equal(Level.Normal, OverlayLayout.Build(config, readings, English)[0].Level);
    }
}
