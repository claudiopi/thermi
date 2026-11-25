using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests;

/// <summary>
/// The two things thermi reads from outside: lines from the sensor host and the user's config file.
/// Neither may crash the overlay, whatever it contains.
/// </summary>
public sealed class FileFormatTests
{
    [Fact]
    public void BrokenSensorValuesTravelAndShowAsMissing()
    {
        var snapshot = new Snapshot(
        [
            new RawSensor("/a", "Cpu", "CPU", "Load", "CPU Total", double.NaN),
            new RawSensor("/b", "Cpu", "CPU", "Power", "Package", double.PositiveInfinity),
        ]);

        var back = SnapshotJson.Read(SnapshotJson.Write(snapshot))!;

        Assert.All(back.Sensors, sensor => Assert.Null(new Reading(SensorKind.Load, sensor.Value).Usable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"sensors\":null}")]
    [InlineData("{\"sensors\":[null]}")]
    [InlineData("{\"sensors\":[{\"id\":\"/a\",\"value\":\"hot\"}]}")]
    [InlineData("{\"sensors\":[{\"hardwareType\":\"SuperIO\",\"hardware\":\"x\",\"type\":\"Fan\",\"name\":\"CPU Fan\",\"value\":1}]}")]
    [InlineData("{\"sensors\":[{\"id\":\"/a\",\"hardwareType\":\"SuperIO\",\"hardware\":\"x\",\"type\":\"Fan\",\"value\":1}]}")]
    public void AMalformedLineIsNotASnapshot(string? line)
    {
        Assert.Null(SnapshotJson.Read(line));
    }

    // Read meanwhile, an editor's save may find a file empty or cut short: only text broken before its end is broken for good.
    [Theory]
    [InlineData(null, true)]
    [InlineData("{ \"version\": 2, \"show\": \"tr", true)]
    [InlineData("[1, 2", true)]
    [InlineData("{ \"version\": 2 }", false)]
    [InlineData("null", false)]
    [InlineData("{ broken", false)]
    public void AFileCutShortIsToldFromABrokenOne(string? json, bool unfinished) =>
        Assert.Equal(unfinished, ThermiConfigJson.IsUnfinished(json));

    // With the field a broken rule names, when it is one.
    [Theory]
    [InlineData("[1,2,3]", null)]
    [InlineData("{\"version\":6,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"threshold\":{\"warning\":90,\"critical\":80}}]}]}", "items[0].threshold")]
    [InlineData("{\"version\":6,\"overlays\":[{\"items\":[{\"metric\":\"game.fps\",\"threshold\":{\"warning\":30,\"critical\":60}}]}]}", "items[0].threshold")]
    [InlineData("{\"version\":7}", null)]
    [InlineData("{\"version\":0}", null)]
    [InlineData("{\"version\":\"two\"}", null)]
    [InlineData("{\"version\":2,\"overlays\":[]}", "overlays holds 0")]
    [InlineData("{\"version\":2,\"overlays\":[{\"size\":2.5}]}", "overlays[0].size")]
    [InlineData("{\"version\":2,\"overlays\":[{\"snap\":{\"monitor\":\"DEL-41A8\"}}]}", "spot")]
    [InlineData("{\"version\":2,\"temperature\":\"k\"}", "temperature")]
    [InlineData("{\"version\":2,\"sensorNames\":{\"/a\":\" \"}}", "sensorNames")]
    [InlineData("{\"version\":2,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"sensor\":\"/a\"}]}]}", "items[0] must name one")]
    [InlineData("{\"version\":2,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"auto\":\"cpu.load\"}]}]}", "items[0].auto")]
    [InlineData("{\"version\":2,\"overlays\":[{\"items\":[{\"sensors\":[\"/a\",\"/b\"]}]}]}", "items[0].combine")]
    [InlineData("{\"version\":2,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"combine\":\"max\"}]}]}", "items[0].combine")]
    [InlineData(
        "{\"version\":6,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"warnBy\":{\"metric\":\"cpu.load\",\"sensor\":\"/a\"}}]}]}",
        "items[0].warnBy must name one")]
    // An older file says what the item warns by as its meter, which the problem names, as the file does.
    [InlineData(
        "{\"version\":5,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"meter\":{\"metric\":\"gpu.load\",\"sensor\":\"/a\"}}]}]}",
        "items[0].meter must name one")]
    [InlineData(
        "{\"version\":6,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"warnBy\":{\"metric\":\"cpu.load\",\"auto\":\"cpu.load\"}}]}]}",
        "items[0].warnBy.auto")]
    [InlineData("{\"version\":6,\"overlays\":[{\"items\":[{\"metric\":\"cpu.load\",\"graph\":\"auto\"}]}]}", "items[0].graph")]
    [InlineData("{\"version\":2,\"overlays\":[{\"style\":{\"name\":\"\"}}]}", "style.name")]
    [InlineData("{\"version\":2,\"overlays\":[{\"style\":{\"label\":{\"size\":21,\"weight\":\"bold\"}}}]}", "style.label.size")]
    [InlineData("{\"version\":2,\"overlays\":[{\"style\":{\"opacity\":{\"background\":1.5}}}]}", "style.opacity.background")]
    [InlineData("{\"version\":2,\"overlays\":[{\"style\":{\"colors\":{\"value\":\"#12345\"}}}]}", "style.colors.value")]
    public void ABrokenOrNewerConfigGivesTheDefaultsAndSaysWhy(string json, string? field)
    {
        var (config, problem) = ThermiConfigJson.Read(json);

        Assert.NotNull(problem);
        Assert.Contains(field ?? "", problem.Reason, StringComparison.Ordinal);
        Assert.Equal(OverlayConfig.DefaultItems, Assert.Single(config.Overlays).Items);
    }

    // No file, or nulls written by hand, read as if left out: the defaults, or what is left of the items.
    [Theory]
    [InlineData(null, 7)]
    [InlineData("null", 6)]
    [InlineData("{\"items\":[null,{\"metric\":null},{\"metric\":\"ram.load\"}]}", 1)]
    [InlineData("{\"sources\":{\"fan.cpu\":null},\"items\":[{\"metric\":\"fan.cpu\",\"meter\":\"fan.cpu.duty\"}]}", 1)]
    [InlineData("{\"version\":2,\"overlays\":null}", 6)]
    [InlineData("{\"version\":2,\"overlays\":[null,{\"style\":null,\"items\":null}]}", 6)]
    [InlineData(
        "{\"version\":2,\"overlays\":[{\"style\":{\"opacity\":null},\"items\":[null,{\"metric\":null},{\"sensors\":[null]},{\"metric\":\"ram.load\"}]}]}",
        1)]
    public void NoConfigOrNullsWrittenByHandMeanTheDefaults(string? json, int items)
    {
        var (config, problem) = ThermiConfigJson.Read(json);

        Assert.Null(problem);
        Assert.Equal(items, Assert.Single(config.Overlays).Items.Count);
        // No file at all is a new one, which shows FPS too.
        var defaults = json is null or "" ? OverlayConfig.First.Items : OverlayConfig.DefaultItems;
        Assert.True(items != defaults.Count || config.Overlays[0].Items.SequenceEqual(defaults), "Some items, but not the defaults.");
        Assert.All(config.Overlays[0].Items, item => Assert.Null(item.Sensor));
        Assert.Equal(new StyleConfig(), config.Overlays[0].Style);
        Assert.Null(config.Overlays[0].Position);
    }

    // R40: a file that is no JSON says where, its line counted from 1, and why, without .NET's own place; a value of the
    // wrong kind names its line too; a newer file says which version; every problem reads as one line in either language.
    [Fact]
    public void ABrokenFileSaysItsLineAndReason()
    {
        var (_, syntax) = ThermiConfigJson.Read("{\n  \"version\": 2,\n  \"overlays\": [ { \"items\": [ { ] } ]\n}");
        var (_, kind) = ThermiConfigJson.Read("{\n  \"version\": 2,\n  \"overlays\": [\n    { \"locked\": \"yes\" }\n  ]\n}");
        var (_, newer) = ThermiConfigJson.Read("{ \"version\": 99 }");
        var english = Core.Translations.Texts.English;
        var spanish = Core.Translations.Texts.Of("es");
        var numbers = CultureInfo.InvariantCulture;
        string OneLine(Core.Translations.Texts texts, ConfigProblemKind title, string detail)
        {
            return texts.Format(numbers, ConfigProblemText.OneLine, texts.Of(title), detail);
        }


        Assert.Equal((ConfigProblemKind.Broken, 3L), (syntax!.Kind, syntax.Line));
        Assert.DoesNotContain("LineNumber", syntax.Reason, StringComparison.Ordinal);
        Assert.Equal(
            OneLine(english, ConfigProblemKind.Broken, english.Format(numbers, ConfigProblemText.AtLine, 3, syntax.Reason)),
            syntax.OneLine(english, numbers));
        Assert.Equal((ConfigProblemKind.Broken, 4L), (kind!.Kind, kind.Line));
        Assert.Equal(english.Format(numbers, ConfigProblemText.Newer, 99, ThermiConfig.CurrentVersion), newer!.Detail(english, numbers));
        Assert.Equal(
            OneLine(spanish, ConfigProblemKind.Newer, spanish.Format(numbers, ConfigProblemText.Newer, 99, ThermiConfig.CurrentVersion)),
            newer.OneLine(spanish, numbers));
        Assert.Equal(OneLine(english, ConfigProblemKind.Locked, "in use"), ConfigProblem.Locked("in use").OneLine(english, numbers));
        Assert.Equal(english.Of(ConfigProblemKind.Missing), ConfigProblem.Missing.OneLine(english, numbers));
    }
}
