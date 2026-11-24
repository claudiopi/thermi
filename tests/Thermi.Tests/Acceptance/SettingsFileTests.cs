using System.Globalization;
using System.Text.Json;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Version 2 settings: ids and migration. The file shared by two processes is the Windows tests' own.
/// </summary>
public sealed class SettingsFileTests
{
    private const string WrongTypeSource = """
        {
          "version": 1,
          "items": [ { "metric": "fan.cpu", "meter": "fan.cpu.duty" }, { "metric": "cpu.temperature", "meter": "cpu.temperature" } ],
          "sources": { "fan.cpu": "/lpc/it8689e/0/control/0", "cpu.temperature": "/lpc/it8689e/0/fan/0" }
        }
        """;

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    [Fact]
    public void AVersionOneFileMigratesToVersionTwo()
    {
        var (config, problem) = ThermiConfigJson.Read(TestFiles.Config("v1.json"));
        var wrongType = ThermiConfigJson.Read(WrongTypeSource).Config.Overlays[0];

        Assert.Null(problem);
        Assert.Equal(ThermiConfig.CurrentVersion, config.Version);
        var overlay = Assert.Single(config.Overlays);
        Assert.Equal(
            [
                new ItemConfig
                {
                    Id = Metrics.CpuFan.Id,
                    Name = Texts.English.Of(Metrics.CpuFan.Name),
                    Sensor = "/lpc/it8689e/0/fan/0",
                    Auto = Metrics.CpuFan.Id,
                    Meter = new MeterConfig { Sensor = "/lpc/it8689e/0/control/0", Auto = Metrics.CpuFanDuty.Id },
                    Threshold = new Threshold(65, 85),
                },
                new ItemConfig { Id = Metrics.CpuTemperature.Id, Name = Texts.English.Of(Metrics.CpuTemperature.Name), Metric = Metrics.CpuTemperature.Id, Meter = new MeterConfig { Metric = Metrics.CpuTemperature.Id }, Threshold = new Threshold(70, 90) },
                new ItemConfig { Id = Metrics.RamLoad.Id, Name = Texts.English.Of(Metrics.RamLoad.Name), Metric = Metrics.RamLoad.Id },
            ],
            overlay.Items);
        Assert.Equal(ShownByVersionOne(overlay, new() { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/fan/0" }), Shown(overlay));
        Assert.Equal(
            [new OverlayItem("CPU", ReadingFormatter.Missing, Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id }, new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id }],
            Shown(wrongType));
        Assert.Equal(ShownByVersionOne(wrongType, new() { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/control/0", [Metrics.CpuTemperature.Id] = "/lpc/it8689e/0/fan/0" }), Shown(wrongType));

        // 0.1's own defaults, which listed the fan first, become this version's; a sensor chosen for one of their meters keeps the item.
        var untouched = ThermiConfigJson.Read(UntouchedVersion1("{}")).Config.Overlays[0].Items;
        var chosenDuty = ThermiConfigJson.Read(UntouchedVersion1("""{ "fan.cpu.duty": "/lpc/it8689e/0/control/1" }""")).Config.Overlays[0].Items;
        Assert.Equal([.. OverlayConfig.DefaultItems.OrderBy(item => item.Id != Metrics.CpuFan.Id)], untouched);
        Assert.Equal(
            new ItemConfig
            {
                Id = Metrics.CpuFan.Id,
                Name = Texts.English.Of(Metrics.CpuFan.Name),
                Metric = Metrics.CpuFan.Id,
                Meter = new MeterConfig { Sensor = "/lpc/it8689e/0/control/1", Auto = Metrics.CpuFanDuty.Id },
                Threshold = new Threshold(60, 85),
            },
            chosenDuty[0]);
        Assert.Equal(untouched.Skip(1), chosenDuty.Skip(1));
    }

    [Fact]
    public void AVersionOneFileKeepsItsPlace()
    {
        var unplaced = ThermiConfigJson.Read("{ \"version\": 1 }").Config;
        var placed = ThermiConfigJson.Read(TestFiles.Config("v1.json")).Config;

        Assert.Equal(new SnapConfig(SnapSpot.TaskbarRight), unplaced.Overlays[0].Snapped);
        Assert.Null(placed.Overlays[0].Snapped);
        Assert.Equal(new Position(1665, 1402), placed.Overlays[0].Position);
        Assert.Equal(new SnapConfig(SnapSpot.TaskbarRight), Reread(unplaced).Overlays[0].Snapped);
        Assert.Equal(new Position(1665, 1402), Reread(placed).Overlays[0].Position);
    }

    [Fact]
    public void ItemIdsAreUniqueAndTheSameOnEveryRead()
    {
        const string File = """
            {
              "version": 2,
              "overlays": [
                { "items": [ { "metric": "cpu.load" }, { "id": "mine", "metric": "cpu.temperature" }, { "metric": "cpu.load" } ] },
                { "items": [ { "id": "mine", "metric": "gpu.load" }, { "sensor": "/gpu-nvidia/0/power/0" } ] }
              ]
            }
            """;

        var first = ThermiConfigJson.Read(File).Config;
        var second = ThermiConfigJson.Read(File).Config;

        Assert.Equal(["cpu.load", "mine", "cpu.load-2", "gpu.load", "/gpu-nvidia/0/power/0"], Ids(first));
        Assert.Equal(Ids(first), Ids(second));
        Assert.Equal(Ids(first), IdsInTheFile(ThermiConfigJson.Write(first)));

        // The window names every item the file left unnamed as a new item of it would be named, and a sensor it cannot
        // see yet by its id; the file keeps them unnamed.
        var metrics = new[] { MetricName.CpuLoad, MetricName.CpuTemperature, MetricName.CpuLoad, MetricName.GpuLoad }.Select(Texts.English.Of);
        var named = PageSettings.Of(first, Texts.English, Desktop);
        Assert.Equal([.. metrics, "/gpu-nvidia/0/power/0"], ItemNames(PageSettings.Of(first, Texts.English, known: null)));
        Assert.Equal([.. metrics], ItemNames(named).Take(4));
        Assert.DoesNotContain(ItemNames(named)[4], new[] { null, "/gpu-nvidia/0/power/0" });
        Assert.All(named.Overlays, overlay => Assert.NotNull(overlay.Name));
        Assert.Same(named, PageSettings.Of(named, Texts.English, Desktop));

        // A copied overlay's items take the next free number of their ids; an id made from a sensor is its sensor again.
        var two = ThermiConfigJson.Read(TestFiles.Config("v2.json")).Config;
        var copied = OverlayEdits.Duplicate(OverlayEdits.Add(two).Config, 0).Config;
        Assert.Equal(["fan.cpu-3", "cpu.temperature-4", "/amdcpu/0/temperature/3", "/gpu-nvidia/0/power/0-2"], copied.Overlays[1].Items.Select(item => item.Id));
    }

    [Fact]
    public void IdsInTheFileAreReservedBeforeAnyIsMade()
    {
        const string Above = """
            { "version": 2, "overlays": [ { "items": [ { "metric": "cpu.temperature" }, { "id": "cpu.temperature", "metric": "cpu.temperature" } ] } ] }
            """;
        const string Taken = """
            { "version": 2, "overlays": [ { "items": [ { "id": "cpu.temperature-2", "metric": "cpu.load" }, { "metric": "cpu.temperature" }, { "metric": "cpu.temperature" } ] } ] }
            """;

        Assert.Equal(["cpu.temperature-2", "cpu.temperature"], Ids(ThermiConfigJson.Read(Above).Config));
        Assert.Equal(["cpu.temperature-2", "cpu.temperature", "cpu.temperature-3"], Ids(ThermiConfigJson.Read(Taken).Config));
    }

    [Fact]
    public void AVersionTwoFileRoundTrips()
    {
        var file = TestFiles.Config("v2.json");

        var (config, problem) = ThermiConfigJson.Read(file);

        Assert.Null(problem);
        Assert.Equal(file.ReplaceLineEndings().TrimEnd(), ThermiConfigJson.Write(config).ReplaceLineEndings());
    }

    private static List<string?> ItemNames(ThermiConfig config) => [.. config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Name)];

    private static List<OverlayItem> Shown(OverlayConfig overlay) => [.. OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), English)];

    // Version 1 showed each item by its metric, with the sources naming sensors for all of them at once.
    private static List<OverlayItem> ShownByVersionOne(OverlayConfig migrated, Dictionary<string, string> sources)
    {
        var byMetric = migrated with
        {
            Items = [.. migrated.Items.Select(item => new ItemConfig
            {
                Id = item.Id,
                Metric = item.Metric ?? item.Auto,
                Meter = item.Meter is null ? null : new MeterConfig { Metric = item.Meter.Metric ?? item.Meter.Auto },
                Threshold = item.Threshold,
            })],
        };
        return [.. OverlayLayout.Build(byMetric, SensorMap.Read(Desktop, sources), English)];
    }

    // thermi 0.1 wrote its own defaults the first time it saved, so a file of them was never chosen.
    private static string UntouchedVersion1(string sources) => $$"""
        {
          "version": 1,
          "items": [
            { "metric": "fan.cpu", "meter": "fan.cpu.duty", "threshold": { "warning": 60, "critical": 85 } },
            { "metric": "cpu.temperature", "meter": "cpu.temperature", "threshold": { "warning": 70, "critical": 85 } },
            { "metric": "cpu.load", "meter": "cpu.load", "threshold": { "warning": 60, "critical": 80 } },
            { "metric": "cpu.power" },
            { "metric": "gpu.load", "meter": "gpu.load", "threshold": { "warning": 60, "critical": 80 } },
            { "metric": "ram.used", "meter": "ram.load", "threshold": { "warning": 60, "critical": 80 } }
          ],
          "sources": {{sources}}
        }
        """;

    private static ThermiConfig Reread(ThermiConfig config) => ThermiConfigJson.Read(ThermiConfigJson.Write(config)).Config;

    private static List<string> Ids(ThermiConfig config) => [.. config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id)];

    private static List<string> IdsInTheFile(string json)
    {
        using var file = JsonDocument.Parse(json);
        return [.. file.RootElement.GetProperty("overlays").EnumerateArray()
            .SelectMany(overlay => overlay.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("id").GetString() ?? "")];
    }
}
