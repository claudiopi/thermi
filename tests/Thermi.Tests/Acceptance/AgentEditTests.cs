using System.Text.Json;
using System.Text.Json.Nodes;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// An agent's edit is the settings window's: every edit the window answered in the page's oracle table
/// (<c>src/Thermi.Settings.Web/tests/oracle/edits.json</c>), sent as an agent sends it, ends in the settings the window
/// showed, or in its refusal, from the same start; a null takes a field back as the page's does; and an edit that is none
/// is a bad argument, never a guess.
/// </summary>
public sealed class AgentEditTests
{
    // Edits of a chart's lines and a look's are the window's gestures and its look command's, never an agent's edit; an undo
    // or a look the window kept leaves the rest of a case to the window.
    private static readonly string[] WindowsOwn = ["line", "typedLine", "addLine", "look"];

    private static readonly Snapshot Admin = TestRecordings.Read(TestRecordings.Admin);

    [Fact]
    public void AgentEditsAreTheWindowsEdits()
    {
        var table = JsonNode.Parse(File.ReadAllText(Path.Combine(TestFiles.Root, "src", "Thermi.Settings.Web", "tests", "oracle", "edits.json")))!;
        var starts = table["starts"]!.AsObject();
        var replayed = table["cases"]!.AsArray().Sum(one => Replay(starts[(string)one!["start"]!]!, one["calls"]!.AsArray()));
        Assert.True(replayed > 50, $"Only {replayed} of the table's edits were an agent's.");

        var config = new ThermiConfig();
        foreach (var (set, back) in SetAndBack())
        {
            var changed = Edit(set, config);
            Assert.NotEqual(ThermiConfigJson.Write(config), ThermiConfigJson.Write(changed));
            Assert.Equal(ThermiConfigJson.Write(config), ThermiConfigJson.Write(Edit(back, changed)));
        }

        var placed = Edit("""{"x":2400,"y":300,"kind":"place","overlay":0}""", config);
        Assert.Equal(ThermiConfigJson.Write(OverlayEdits.Drop(config, 0, new Position(2400, 300)).Config), ThermiConfigJson.Write(placed));
        var lines = Edit("""{"kind":"threshold","item":"cpu.temperature","warning":70,"critical":90}""", config);
        Assert.Equal(new Threshold(70, 90), lines.Overlays[0].Items[0].Threshold);
        // A line left out stays; null takes it out.
        Assert.Equal(new Threshold(75, 90), Edit("""{"kind":"threshold","item":"cpu.temperature","warning":75}""", lines).Overlays[0].Items[0].Threshold);
        var critical = Edit("""{"kind":"threshold","item":"cpu.temperature","warning":null}""", lines);
        Assert.Equal(new Threshold(critical: 90), critical.Overlays[0].Items[0].Threshold);
        Assert.Equal(config.Overlays[0].Items[0].Threshold, Edit("""{"kind":"resetLines","item":"cpu.temperature"}""", lines).Overlays[0].Items[0].Threshold);
        Assert.Equal(ThermiConfigJson.Write(config), ThermiConfigJson.Write(Edit("""{"kind":"reset"}""", Edit("""{"kind":"newOverlay"}""", lines))));
        // A move however far stops at the end.
        Assert.Equal("cpu.load", Edit("""{"kind":"moveItem","item":"cpu.load","by":2147483647}""", config).Overlays[0].Items[^1].Id);
        Assert.All(NoEdits(), json => Assert.Throws<AgentInputException>(() => Edit(json, config)));
        (string Json, Refusal Refusal)[] refused =
        [
            ("""{"kind":"overlay","overlay":0,"field":"name","value":null}""", Refusal.NameEmpty),
            ("""{"kind":"item","item":"cpu.load","field":"name","value":" "}""", Refusal.NameEmpty),
            ("""{"kind":"item","item":"nobody","field":"label","value":null}""", Refusal.NotFound),
        ];
        foreach (var (json, refusal) in refused)
        {
            // Said by its key and its English text, whatever the settings' language.
            var answer = AgentViews.Edited(AgentEdits.Of(json, Admin, Texts.English, () => new HashSet<int>())(config));
            Assert.Equal((false, Texts.Key(refusal), Texts.English.Of(refusal)), (answer.Done, answer.Refusal, answer.Message));
        }

        Assert.True(AgentViews.Edited(new EditResult(lines)).Done);
    }

    // Each case's edits from its start, as far as they are an agent's, each to the settings the window showed after it.
    private static int Replay(JsonNode start, JsonArray calls)
    {
        if (start["problem"] is not null)
        {
            return 0;
        }

        var (snapshot, texts) = (SnapshotOf(start), start["texts"]?.GetValue<string>() == "pseudo" ? Texts.Pseudo() : Texts.English);
        // Read as the window reads the file the replay wrote.
        var config = ThermiConfigJson.Read(ThermiConfigJson.Write(start["settings"].Deserialize<ThermiConfig>(BridgeCalls.Options)!)).Config;
        var shown = start["shown"];
        Assert.True(JsonNode.DeepEquals(shown, Shown(config, texts, snapshot)), "A start of the table no longer shows as the window showed it.");
        var edits = calls.TakeWhile(call => IsAgents(call!)).Where(call => (string)call!["method"]! == "edit").ToList();
        foreach (var call in edits)
        {
            var edit = AgentEdits.Of(call!["args"]![0]!.ToJsonString(), snapshot, texts, () => new HashSet<int>())(config);
            Assert.Equal((string?)call["answer"]!["result"], AgentViews.Edited(edit).Refusal);
            config = edit.Refused is null ? edit.Config : config;
            shown = call["answer"]!["settings"] ?? shown;
            Assert.True(JsonNode.DeepEquals(shown, Shown(config, texts, snapshot)), $"The agent's {call["args"]![0]} is not the window's.");
        }

        return edits.Count;
    }

    // Fields the page's tests never take back, set, then sent null, which leaves them as they were.
    private static IEnumerable<(string Set, string Back)> SetAndBack()
    {
        var fan = Admin.Sensors.Last(sensor => sensor.Type == "Fan").Id;
        var load = Admin.Sensors.First(sensor => sensor.Type == "Load").Id;
        (string Kind, string Target, string Field, string Value)[] fields =
        [
            ("thermi", "", "windowTheme", "\"dark\""),
            ("thermi", "", "temperature", "\"f\""),
            ("overlay", "\"overlay\":0,", "graph", "\"line\""),
            ("style", "\"overlay\":0,", "gap", "12"),
            ("item", "\"item\":\"fan.cpu\",", "sensor", $"\"{fan}\""),
            ("item", "\"item\":\"cpu.load\",", "warnBy", $$"""{"sensor":"{{load}}"}"""),
            ("item", "\"item\":\"cpu.load\",", "smooth", "5"),
            ("item", "\"item\":\"cpu.load\",", "decimals", "1"),
            ("item", "\"item\":\"cpu.load\",", "graph", "\"bars\""),
            ("item", "\"item\":\"cpu.power\",", "offset", "2"),
        ];
        return fields.Select(each => (
            $$"""{"kind":"{{each.Kind}}",{{each.Target}}"field":"{{each.Field}}","value":{{each.Value}}}""",
            $$"""{"kind":"{{each.Kind}}",{{each.Target}}"field":"{{each.Field}}","value":null}"""));
    }

    private static string[] NoEdits() =>
    [
        "not json",
        "[]",
        """{"item":"cpu.load"}""",
        """{"kind":"nothing"}""",
        """{"kind":"line","item":"cpu.load","line":"warning","value":50}""",
        """{"kind":"look","overlay":0,"name":"Night"}""",
        """{"kind":"threshold","warning":70}""",
        """{"kind":"place","overlay":0,"x":1}""",
        """{"kind":"item","item":"cpu.load","field":"nothing","value":1}""",
        """{"kind":"overlay","overlay":0,"field":"nothing","value":1}""",
        """{"kind":"thermi","field":"nothing","value":1}""",
        """{"kind":"style","overlay":0,"field":"nothing","value":1}""",
        """{"kind":"moveItem","by":1}""",
        """{"kind":"removeItem","item":null}""",
    ];

    private static ThermiConfig Edit(string json, ThermiConfig config)
    {
        var edit = AgentEdits.Of(json, Admin, Texts.English, () => new HashSet<int>())(config);
        Assert.Null(edit.Refused);
        return edit.Config;
    }

    private static bool IsAgents(JsonNode call) => (string)call["method"]! switch
    {
        "edit" => !WindowsOwn.Contains((string)call["args"]![0]!["kind"]!),
        "gesture" => true,
        _ => false,
    };

    private static JsonNode? Shown(ThermiConfig config, Texts texts, Snapshot snapshot) =>
        JsonSerializer.SerializeToNode(PageSettings.Of(config, texts, snapshot), BridgeCalls.Options);

    // As the window's replay feeds it: a recording by name, or the sensors themselves; none while the service is stopped.
    private static Snapshot SnapshotOf(JsonNode start) => start["service"]?.GetValue<string>() == "stopped"
        ? new Snapshot([])
        : start["sensors"] is JsonValue name
            ? TestRecordings.Read($"ryzen-9950x-rtx-5060-ti-{name.GetValue<string>()}.json")
            : new Snapshot(start["sensors"].Deserialize<List<RawSensor>>(JsonSerializerOptions.Web) ?? []);
}
