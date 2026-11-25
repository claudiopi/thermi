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

    // The values of the CPU's temperature, fan, load and power, the GPU's load and the RAM used, then the segments lit of
    // the meters of the temperature, the fan and the RAM. The i9's CPU temperature is its package's, not a core's nor the
    // board's "CPU Core" at 50 °C, and its CPU fan the Nuvoton's second input, not its first, which reads 0 RPM.
    [Theory]
    [InlineData(TestRecordings.Admin, "77 °C|1,087 RPM|9 %|71 W|3 %|27.2 GB", "4 1 2")]
    [InlineData(TestRecordings.IntelAdmin, "57 °C|720 RPM|5 %|29 W|5 %|15.0 GB", "3 1 1")]
    public void ShowsTheDefaultOverlayFromARecording(string recording, string values, string lit)
    {
        var shown = values.Split('|');
        var meters = lit.Split(' ').Select(each => int.Parse(each, CultureInfo.InvariantCulture)).ToArray();

        // The loads' graphs are lists, which records compare by reference; SettingsFileTests says which items have one.
        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(TestRecordings.Read(recording), NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("CPU", shown[0], Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id, Lit = meters[0] },
                new OverlayItem(null, shown[1], Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id, Lit = meters[1] },
                new OverlayItem(null, shown[2], Level.Normal, "100 %") { Id = Metrics.CpuLoad.Id },
                new OverlayItem(null, shown[3], Level.Normal, "888 W") { Id = Metrics.CpuPower.Id },
                new OverlayItem("GPU", shown[4], Level.Normal, "100 %") { Id = Metrics.GpuLoad.Id },
                new OverlayItem("RAM", shown[5], Level.Normal, "888.8 GB") { Id = Metrics.RamUsed.Id, Lit = meters[2] },
            ],
            items.Select(item => item with { Graph = null }));
    }

    [Fact]
    public void WithoutAdminRightsTheCpuShowsAsMissingNotAsZero()
    {
        var recording = TestRecordings.Read(TestRecordings.NoAdmin);

        var items = OverlayLayout.Build(new OverlayConfig(), SensorMap.Read(recording, NoSources), English);

        Assert.Equal(
            [
                new OverlayItem("CPU", ReadingFormatter.Missing, Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id, Lit = 0 },
                new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id, Lit = 0 },
                new OverlayItem(null, "10 %", Level.Normal, "100 %") { Id = Metrics.CpuLoad.Id },
                new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "888 W") { Id = Metrics.CpuPower.Id },
                new OverlayItem("GPU", "3 %", Level.Normal, "100 %") { Id = Metrics.GpuLoad.Id },
                new OverlayItem("RAM", "27.2 GB", Level.Normal, "888.8 GB") { Id = Metrics.RamUsed.Id, Lit = 2 },
            ],
            items.Select(item => item with { Graph = null }));
    }

    // A sensor that is gone, or named for a metric of another kind, leaves the metric missing: never another sensor's
    // value, nor one in the wrong unit. Without a metric, every metric is. An Intel core's distance to its limit, TjMax,
    // falls as the core heats, so it is never the CPU's temperature.
    [Theory]
    [InlineData("the desktop without Memory Used", null, null, "ram.used")]
    [InlineData("the i9's distances to TjMax alone", null, null, "cpu.temperature")]
    [InlineData("the desktop", "cpu.temperature", "/lpc/it8689e/0/fan/0", "cpu.temperature")]
    [InlineData("the desktop", "cpu.temperature", "/amdcpu/9/temperature/9", "cpu.temperature")]
    [InlineData("nothing", null, null, null)]
    public void AMetricWithoutItsSensorIsMissingNeverGuessed(string snapshot, string? named, string? sensor, string? metric)
    {
        var read = snapshot switch
        {
            "the desktop" => Desktop,
            "the desktop without Memory Used" => new Snapshot([.. Desktop.Sensors.Where(each => each.Name != "Memory Used")]),
            "the i9's distances to TjMax alone" => new Snapshot(
                [.. TestRecordings.Read(TestRecordings.IntelAdmin).Sensors.Where(each => each.Name.EndsWith("Distance to TjMax", StringComparison.Ordinal))]),
            _ => new Snapshot([]),
        };
        var sources = named is null ? NoSources : new Dictionary<string, string>(StringComparer.Ordinal) { [named] = sensor! };

        var readings = SensorMap.Read(read, sources);

        Assert.All(metric is null ? Metrics.All.Select(each => each.Id) : [metric], id => Assert.Null(readings[id].Value));
    }

    // A clock of the CPU's cores, never its bus's, and a fan wherever the PC reports one, as a laptop does on its controller.
    [Theory]
    [InlineData("cpu.clock", 4_600)]
    [InlineData("fan.cpu", 2_400)]
    public void AnIntelLaptopShowsItsCoresClockAndItsFan(string metric, double value)
    {
        var readings = SensorMap.Read(MadeUpHardware.IntelLaptop(), NoSources);

        Assert.Equal(value, readings[metric].Value);
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
        var byCcd2 = new WarnByConfig { Sensor = Ccd2, Auto = Metrics.CpuTemperature.Id };
        var overlay = new OverlayConfig
        {
            Items =
            [
                new ItemConfig { Id = "ccd1", Sensor = Ccd1, Auto = Metrics.CpuTemperature.Id, Label = "CCD1" },
                new ItemConfig { Id = "ccd2", Sensor = Ccd2, Auto = Metrics.CpuTemperature.Id, Label = "CCD2", Threshold = new Threshold(40, 60) },
                new ItemConfig { Id = "load", Metric = Metrics.CpuLoad.Id, Label = "LOAD", WarnBy = byCcd2, Threshold = new Threshold(40, 60) },
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
        var config = new OverlayConfig
        {
            Graph = GraphStyle.None,
            Items = [new ItemConfig { Metric = "gpu.hotspot" }, new ItemConfig { Metric = Metrics.RamLoad.Id }],
        };

        var items = OverlayLayout.Build(config, SensorMap.Read(Desktop, NoSources), English);

        Assert.Equal([new OverlayItem("RAM", "44 %", Level.Normal, "100 %")], items);
    }

    // The owner, 2026-11-24: an item draws its own choice beside its value, else its overlay's, whose Auto is a line for a
    // load warned by itself and bars for the rest; one lines cannot judge draws nothing, whatever is chosen. Only an item
    // that draws nothing underlines its warning, since bars or a line already show it.
    [Theory]
    [InlineData(GraphStyle.Auto, null, "cpu.load", SensorKind.Load, GraphStyle.Line)]
    [InlineData(GraphStyle.Auto, null, "cpu.temperature", SensorKind.Temperature, GraphStyle.Bars)]
    [InlineData(GraphStyle.Auto, null, "ram.used", SensorKind.Data, GraphStyle.Bars)]
    [InlineData(GraphStyle.Auto, null, "cpu.power", SensorKind.Power, GraphStyle.None)]
    [InlineData(GraphStyle.None, null, "cpu.temperature", SensorKind.Temperature, GraphStyle.None)]
    [InlineData(GraphStyle.Bars, null, "cpu.load", SensorKind.Load, GraphStyle.Bars)]
    [InlineData(GraphStyle.Line, null, "fan.cpu", SensorKind.Fan, GraphStyle.Line)]
    [InlineData(GraphStyle.Line, GraphStyle.Bars, "cpu.load", SensorKind.Load, GraphStyle.Bars)]
    [InlineData(GraphStyle.None, GraphStyle.Line, "cpu.temperature", SensorKind.Temperature, GraphStyle.Line)]
    [InlineData(GraphStyle.Bars, GraphStyle.None, "ram.used", SensorKind.Data, GraphStyle.None)]
    [InlineData(GraphStyle.Line, GraphStyle.Line, "cpu.power", SensorKind.Power, GraphStyle.None)]
    public void AnItemDrawsItsOverlaysGraphUnlessItChoosesItsOwn(GraphStyle chosen, GraphStyle? own, string metric, SensorKind kind, GraphStyle drawn)
    {
        // The RAM in use is warned by the RAM's load, as thermi makes it.
        var warnBy = metric == Metrics.RamUsed.Id ? new WarnByConfig { Metric = Metrics.RamLoad.Id } : null;
        var item = new ItemConfig { Id = metric, Metric = metric, WarnBy = warnBy, Graph = own, Threshold = new Threshold(50, null) };
        var overlay = new OverlayConfig { Graph = chosen, Items = [item] };
        var readings = new Dictionary<string, Reading>(StringComparer.Ordinal) { [metric] = new(kind, 60), [Metrics.RamLoad.Id] = new(SensorKind.Load, 60) };

        var shown = OverlayLayout.Build(overlay, readings, English).Single();

        Assert.Equal(drawn, OverlayLayout.DrawnOf(overlay, item));
        Assert.Equal((drawn == GraphStyle.Line, drawn == GraphStyle.Bars), (shown.Graph is not null, shown.Lit is not null));
        Assert.Equal(
            drawn == GraphStyle.None && kind != SensorKind.Power,
            LevelMarks.Of(OverlayLook.Of(new StyleConfig(), lightTaskbar: false), shown).Kind == MarkKind.Underline);
    }
}
