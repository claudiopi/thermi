using Thermi.Core;

namespace Thermi.Tests;

/// <summary>
/// The two things thermi reads from outside: lines from the sensor host and the user's config file.
/// Neither may crash the overlay, whatever it contains.
/// </summary>
public sealed class FileFormatTests
{
    [Fact]
    public void ASnapshotSurvivesThePipe()
    {
        var snapshot = new Snapshot([new RawSensor("/ram/load/0", "Memory", "Generic Memory", "Load", "Memory", 42.5)]);

        var back = SnapshotJson.Read(SnapshotJson.Write(snapshot));

        Assert.NotNull(back);
        Assert.Equal(snapshot.Sensors, back.Sensors);
    }

    [Fact]
    public void BrokenSensorValuesTravelAndShowAsMissing()
    {
        var snapshot = new Snapshot([new RawSensor("/a", "Cpu", "CPU", "Load", "CPU Total", double.NaN), new RawSensor("/b", "Cpu", "CPU", "Power", "Package", double.PositiveInfinity)]);

        var back = SnapshotJson.Read(SnapshotJson.Write(snapshot))!;

        Assert.All(back.Sensors, sensor => Assert.Null(new Reading(SensorKind.Load, sensor.Value).Usable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"sensors\":null}")]
    [InlineData("{\"sensors\":[null]}")]
    [InlineData("{\"sensors\":[{\"id\":\"/a\",\"value\":\"hot\"}]}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"sensors\":[")]
    public void AMalformedLineIsNotASnapshot(string? line)
    {
        Assert.Null(SnapshotJson.Read(line));
    }

    [Fact]
    public void AnOversizedLineIsRejectedUnread()
    {
        Assert.Null(SnapshotJson.Read(new string(' ', SnapshotJson.MaxLineLength) + "{\"sensors\":[]}"));
    }

    [Fact]
    public void TheConfigSurvivesASaveAndALoad()
    {
        var config = new OverlayConfig
        {
            Position = new Position(-1200, 1402),
            Sources = new Dictionary<string, string>(StringComparer.Ordinal) { [Metrics.CpuFan.Id] = "/lpc/it8689e/0/fan/1" },
        };

        var (back, problem) = OverlayConfigJson.Read(OverlayConfigJson.Write(config));

        Assert.Null(problem);
        Assert.Equal(config.Position, back.Position);
        Assert.Equal(config.Items, back.Items);
        Assert.Equal(config.Sources, back.Sources);
    }

    // A file as version 1 wrote it, committed: every later version must still read it the same way.
    [Fact]
    public void AVersionOneFileStillReads()
    {
        var (config, problem) = OverlayConfigJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configs", "v1.json")));

        Assert.Null(problem);
        Assert.Equal(new Position(1665, 1402), config.Position);
        Assert.Equal(
            [
                new ItemConfig(Metrics.CpuFan.Id, Metrics.CpuFanDuty.Id, new Threshold(60, 85)),
                new ItemConfig(Metrics.CpuTemperature.Id, Metrics.CpuTemperature.Id, new Threshold(70, 90)),
                new ItemConfig(Metrics.RamLoad.Id),
            ],
            config.Items);
        Assert.Equal("/lpc/it8689e/0/fan/0", config.Sources[Metrics.CpuFan.Id]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoConfigMeansTheDefaults(string? json)
    {
        var (config, problem) = OverlayConfigJson.Read(json);

        Assert.Null(problem);
        Assert.Equal(new OverlayConfig().Items, config.Items);
        Assert.Null(config.Position);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"items\":[{\"metric\":\"cpu.load\",\"threshold\":{\"warning\":90,\"critical\":80}}]}")]
    [InlineData("{\"version\":2}")]
    public void ABrokenOrNewerConfigGivesTheDefaultsAndSaysWhy(string json)
    {
        var (config, problem) = OverlayConfigJson.Read(json);

        Assert.NotNull(problem);
        Assert.Equal(new OverlayConfig().Items, config.Items);
    }

    [Theory]
    [InlineData("{\"items\":null,\"sources\":null}", 6)]
    [InlineData("{\"items\":[null,{\"metric\":null},{\"metric\":\"ram.load\"}]}", 1)]
    public void NullsWrittenByHandAreTreatedAsLeftOut(string json, int items)
    {
        var (config, problem) = OverlayConfigJson.Read(json);

        Assert.Null(problem);
        Assert.Equal(items, config.Items.Count);
        Assert.Empty(config.Sources);
    }

    [Fact]
    public void AConfigWithOnlySomeSettingsKeepsTheDefaultsForTheRest()
    {
        var (config, problem) = OverlayConfigJson.Read("{\"position\":{\"x\":10,\"y\":20}}");

        Assert.Null(problem);
        Assert.Equal(new Position(10, 20), config.Position);
        Assert.Equal(new OverlayConfig().Items, config.Items);
    }
}
