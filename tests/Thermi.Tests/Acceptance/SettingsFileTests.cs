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
/// The settings file: ids and migration. The file shared by two processes is the Windows tests' own.
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
                    Sensor = "/lpc/it8689e/0/fan/0",
                    Auto = Metrics.CpuFan.Id,
                    WarnBy = new WarnByConfig { Sensor = "/lpc/it8689e/0/control/0", Auto = Metrics.CpuFanDuty.Id },
                    Threshold = new Threshold(65, 85),
                },
                new ItemConfig { Id = Metrics.CpuTemperature.Id, Metric = Metrics.CpuTemperature.Id, Threshold = new Threshold(70, 90) },
                // Version 1 drew nothing beside an item without a meter.
                new ItemConfig { Id = Metrics.RamLoad.Id, Metric = Metrics.RamLoad.Id, Graph = GraphStyle.None },
            ],
            overlay.Items);
        Assert.Equal(ShownByVersionOne(overlay, new() { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/fan/0" }), Shown(overlay));
        // Its items store no name, so a Spanish window names them in Spanish.
        Assert.Equal(Texts.Of("es").Of(MetricName.CpuTemperature), ItemNames(PageSettings.Of(config, Texts.Of("es"), Desktop))[1]);
        Assert.Equal(
            [
                new OverlayItem("CPU", ReadingFormatter.Missing, Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id, Lit = 0 },
                new OverlayItem(null, ReadingFormatter.Missing, Level.Normal, "100 °C") { Id = Metrics.CpuTemperature.Id, Lit = 0 },
            ],
            Shown(wrongType));
        Assert.Equal(
            ShownByVersionOne(
                wrongType,
                new() { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/control/0", [Metrics.CpuTemperature.Id] = "/lpc/it8689e/0/fan/0" }),
            Shown(wrongType));

        // 0.1's own defaults, which listed the fan first, become this version's; a sensor chosen for one of their meters keeps
        // the item, and its lines, 0.1's defaults, become this version's calm ones, while lines the user moved stay, as above.
        var untouched = ThermiConfigJson.Read(UntouchedVersion1("{}")).Config.Overlays[0].Items;
        var chosenDuty = ThermiConfigJson.Read(UntouchedVersion1("""{ "fan.cpu.duty": "/lpc/it8689e/0/control/1" }""")).Config.Overlays[0].Items;
        Assert.Equal([.. OverlayConfig.DefaultItems.OrderBy(item => item.Id != Metrics.CpuFan.Id)], untouched);
        Assert.Equal(
            new ItemConfig
            {
                Id = Metrics.CpuFan.Id,
                Metric = Metrics.CpuFan.Id,
                WarnBy = new WarnByConfig { Sensor = "/lpc/it8689e/0/control/1", Auto = Metrics.CpuFanDuty.Id },
                Threshold = Threshold.Default(Metrics.CpuFanDuty),
            },
            chosenDuty[0]);
        Assert.Equal(untouched.Skip(1), chosenDuty.Skip(1));
    }

    [Fact]
    public void AVersionOneFileKeepsItsPlace()
    {
        var unplaced = ThermiConfigJson.Read("{ \"version\": 1 }").Config;
        var placed = ThermiConfigJson.Read(TestFiles.Config("v1.json")).Config;

        Assert.Equal(new SnapConfig(SnapSpot.Taskbar), unplaced.Overlays[0].Snapped);
        Assert.Null(placed.Overlays[0].Snapped);
        Assert.Equal(new Position(1665, 1402), placed.Overlays[0].Position);
        Assert.Equal(new SnapConfig(SnapSpot.Taskbar), Reread(unplaced).Overlays[0].Snapped);
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
        var two = ThermiConfigJson.Read(TestFiles.Config("v6.json")).Config;
        var copied = OverlayEdits.Duplicate(OverlayEdits.Add(two).Config, 0).Config;
        Assert.Equal(
            ["fan.cpu-3", "cpu.temperature-4", "/amdcpu/0/temperature/3", "/gpu-nvidia/0/power/0-2"],
            copied.Overlays[1].Items.Select(item => item.Id));
    }

    [Fact]
    public void IdsInTheFileAreReservedBeforeAnyIsMade()
    {
        const string Above = """
            { "version": 2, "overlays": [ { "items": [ { "metric": "cpu.temperature" }, { "id": "cpu.temperature", "metric": "cpu.temperature" } ] } ] }
            """;
        const string Taken = """
            { "version": 2, "overlays": [ { "items": [
              { "id": "cpu.temperature-2", "metric": "cpu.load" }, { "metric": "cpu.temperature" }, { "metric": "cpu.temperature" } ] } ] }
            """;

        Assert.Equal(["cpu.temperature-2", "cpu.temperature"], Ids(ThermiConfigJson.Read(Above).Config));
        Assert.Equal(["cpu.temperature-2", "cpu.temperature", "cpu.temperature-3"], Ids(ThermiConfigJson.Read(Taken).Config));
    }

    // What earlier builds wrote unchosen takes this version's: 0.1's default critical line its lines, thermi's names in any
    // language no name, so they read in the window's, and a custom look's 10 px labels Standard's.
    [Fact]
    public void AVersionTwoFileMigratesToVersionSix()
    {
        var spanish = Texts.Of("es");
        var file = $$"""
            { "version": 2, "overlays": [
              { "name": "{{spanish.Of(NameText.Taskbar)}} 2", "style": { "preset": "custom", "label": { "size": 10, "weight": "regular" } },
                "items": [ { "name": "{{spanish.Of(MetricName.CpuTemperature)}}", "metric": "cpu.temperature" },
                  { "metric": "ram.used", "meter": { "metric": "ram.load" }, "threshold": { "warning": 69, "critical": 80 } },
                  { "metric": "cpu.load", "meter": { "metric": "cpu.load" } },
                  { "metric": "gpu.load", "meter": { "metric": "gpu.load" }, "threshold": { "warning": 50 } } ] },
              { "name": "{{Texts.English.Of(SnapSpot.TopRight)}}",
                "style": { "preset": "custom", "label": { "size": 10, "weight": "regular" }, "value": { "size": 16, "weight": "bold" } } } ] }
            """;

        var (config, problem) = ThermiConfigJson.Read(TestFiles.Config("v2.json"));
        var other = ThermiConfigJson.Read(file).Config;

        Assert.Null(problem);
        Assert.Equal(ThermiConfig.CurrentVersion, config.Version);
        Assert.Equal(["Night desk", null], config.Overlays.Select(overlay => overlay.Name));
        Assert.Equal([null, null, "CCDs", "GPU power", "CPU temperature 2", null], ItemNames(config));
        Assert.Equal(
            [new Threshold(85), Threshold.Default(Metrics.CpuTemperature), new Threshold(70, 90), null, null, null],
            config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Threshold));
        Assert.Equal(spanish.Of(MetricName.CpuTemperature), ItemNames(PageSettings.Of(config, spanish, Desktop))[1]);
        Assert.Equal([null, null], other.Overlays.Select(overlay => overlay.Name));
        Assert.All(ItemNames(other), Assert.Null);
        Assert.Equal([StylePreset.Standard, StylePreset.Custom], other.Overlays.Select(overlay => overlay.Style.Preset));
        Assert.Equal(StylePresets.Of(StylePreset.Standard).Label, StylePresets.Text(other.Overlays[1].Style).Label);

        // QA r4 (R4-04): a warning nudged under 0.1's critical line goes with it, as a new RAM item starts without lines.
        Assert.Null(other.Overlays[0].Items[1].Threshold);

        // The owner: a load as thermi made it shows its line, as a new one does; one given lines keeps its bars, and an
        // item without a meter draws nothing still.
        var drawn = other.Overlays[0].Items.Select(item => OverlayLayout.DrawnOf(other.Overlays[0], item));
        Assert.Equal([GraphStyle.None, GraphStyle.Bars, GraphStyle.Line, GraphStyle.Bars], drawn);
        Assert.Equal([GraphStyle.None, null, null, GraphStyle.Bars], other.Overlays[0].Items.Select(item => item.Graph));
        var made = new OverlayConfig();
        Assert.Equal(
            [Metrics.CpuLoad.Id, Metrics.GpuLoad.Id],
            made.Items.Where(item => OverlayLayout.DrawnOf(made, item) == GraphStyle.Line).Select(item => item.Id));
    }

    // The owner, 2026-11-24: version 6 splits the meter in two, what an item warns by and what it draws beside its value,
    // and a version 5 file draws and warns as it did. The everything file of version 6 is this one migrated, with its
    // first overlay's bars chosen, which draw what it drew already, and the window's theme, which version 6 brought.
    [Fact]
    public void AVersionFiveFileMigratesToVersionSix()
    {
        var (config, problem) = ThermiConfigJson.Read(TestFiles.Config("v5.json"));
        var six = ThermiConfigJson.Read(TestFiles.Config("v6.json")).Config;
        var migrated = six with { WindowTheme = WindowTheme.System, Overlays = [six.Overlays[0] with { Graph = GraphStyle.Auto }, six.Overlays[1]] };

        Assert.Null(problem);
        Assert.Equal(ThermiConfigJson.Write(migrated), ThermiConfigJson.Write(config));
        Assert.Equal(
            [GraphStyle.Bars, GraphStyle.Line, GraphStyle.Bars, GraphStyle.None, GraphStyle.None, GraphStyle.Bars],
            config.Overlays.SelectMany(overlay => overlay.Items.Select(item => OverlayLayout.DrawnOf(overlay, item))));
        Assert.Equal(
            config.Overlays.SelectMany(overlay => overlay.Items.Select(item => OverlayLayout.DrawnOf(overlay, item))),
            six.Overlays.SelectMany(overlay => overlay.Items.Select(item => OverlayLayout.DrawnOf(overlay, item))));
    }

    // The owner, 2026-11-24: the taskbar's three places of versions 2 to 4 become its one free place, a migrated file
    // written as version 5. Its right place stays beside the notification area, its left one the margin in from the start,
    // and its center, which the app buttons decided, goes beside the notification area; a corner stays as it was.
    [Theory] // Rows: each a place A307 states.
    [InlineData(4, """{ "spot": "taskbar-right" }""", """{"spot":"taskbar"}""")]
    [InlineData(3, """{ "spot": "taskbar-left", "monitor": "DEL-41A8" }""", """{"spot":"taskbar","monitor":"DEL-41A8","offset":8}""")]
    [InlineData(4, """{ "spot": "taskbar-center" }""", """{"spot":"taskbar"}""")]
    [InlineData(4, """{ "spot": "top-left" }""", """{"spot":"top-left"}""")]
    public void AVersionFourTaskbarPlaceBecomesTheFreeTaskbarPlace(int version, string snap, string migrated)
    {
        var (config, problem) = ThermiConfigJson.Read($$"""{ "version": {{version}}, "overlays": [ { "snap": {{snap}} } ] }""");

        Assert.Null(problem);
        Assert.Equal(ThermiConfig.CurrentVersion, config.Version);
        var written = JsonDocument.Parse(ThermiConfigJson.Write(config)).RootElement.GetProperty("overlays")[0].GetProperty("snap");
        Assert.Equal(migrated, JsonSerializer.Serialize(written));
    }

    [Fact]
    public void AVersionSixFileRoundTrips()
    {
        var file = TestFiles.Config("v6.json");

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
                WarnBy = item.WarnBy is null ? null : new WarnByConfig { Metric = item.WarnBy.Metric ?? item.WarnBy.Auto },
                Threshold = item.Threshold,
                Graph = item.Graph,
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

    // A version rolled back to finds the copy of the settings kept in its format, and a new copy never takes an old one's name.
    [Fact]
    public void ASettingsCopyIsFoundAgainByItsFormat()
    {
        var file = Path.Combine("settings", "config.json");
        string[] kept =
        [
            "config.v3-2026-01-01-000000.json", "config.v4-2026-01-02-000000.json", "config.v4-2026-03-01-000000.json",
            "config.broken-2026-04-01-000000.json",
        ];
        Assert.Equal("config.v4-2026-03-01-000000.json", KeptCopies.Newest(kept.Select(name => Path.Combine("settings", name)), file, 4));
        Assert.Null(KeptCopies.Newest(kept, file, 5));
        var taken = new HashSet<string>(StringComparer.Ordinal) { Path.Combine("settings", "config.v5-2026-11-24-210000.json") };
        var at = new DateTimeOffset(2026, 11, 24, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(Path.Combine("settings", "config.v5-2026-11-24-210000-2.json"), KeptCopies.Name(file, "v5", at, taken.Contains));
    }
}
