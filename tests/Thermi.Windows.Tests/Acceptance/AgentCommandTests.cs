using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Tests.Agents;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// <c>thermi-cli</c>'s commands as an agent runs them (<c>docs/specs/003-agents/spec.md</c>): one JSON document or nothing
/// on stdout, why on stderr, an exit code for each way a command ends, and edits written as the window writes them.
/// </summary>
public sealed class AgentCommandTests : IDisposable
{
    private const string TooHigh = """{"kind":"threshold","item":"cpu.temperature","warning":95,"critical":90}""";

    private readonly AgentRig _rig = new();

    public void Dispose() => _rig.Dispose();

    public enum Ending
    {
        Done,
        Stopped,
        Broken,
    }

    [Theory]
    [InlineData(Ending.Done, 0, "status")]
    [InlineData(Ending.Done, 0, "read", "cpu.temperature")]
    [InlineData(Ending.Done, 0, "sensors", "--search", "cpu")]
    [InlineData(Ending.Done, 0, "config")]
    [InlineData(Ending.Done, 0, "monitors")]
    [InlineData(Ending.Done, 0, "looks")]
    [InlineData(Ending.Done, 0, "start-with-windows")]
    [InlineData(Ending.Done, 0, "edit", """{"kind":"item","item":"cpu.temperature","field":"name","value":"Tdie"}""")]
    [InlineData(Ending.Done, 1, "edit", TooHigh)]
    [InlineData(Ending.Done, 1, "look", "wear", "nobody's")]
    [InlineData(Ending.Done, 2, "read", "no.such.metric")]
    [InlineData(Ending.Done, 2, "read", "--seconds", "0")]
    [InlineData(Ending.Done, 2, "edit", "not json")]
    [InlineData(Ending.Done, 2, "edit", """{"kind":"nothing"}""")]
    [InlineData(Ending.Done, 2, "status", "--schema", "2")]
    [InlineData(Ending.Stopped, 0, "status")]
    [InlineData(Ending.Stopped, 3, "read")]
    [InlineData(Ending.Stopped, 3, "sensors")]
    [InlineData(Ending.Broken, 4, "config")]
    [InlineData(Ending.Broken, 4, "edit", TooHigh)]
    public async Task EveryCommandEndsWithItsCode(Ending ending, int exit, params string[] args)
    {
        if (ending == Ending.Stopped)
        {
            _rig.Sensors = () => new RecordedFeed([], ServiceState.Stopped);
        }

        if (ending == Ending.Broken)
        {
            _rig.Folder.Write("{ \"version\": ");
        }

        var end = await _rig.RunAsync(args);

        Assert.Equal(exit, end.Exit);
        Assert.Equal(exit == 0, end.Error.Length == 0);
        if (end.Out.Length > 0)
        {
            Assert.Single(end.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(AgentSchema.Current, (int)end.Json["schema"]!);
        }

        Assert.Equal(exit is 0 or 1 || args[0] == "config", end.Out.Length > 0);
    }

    // The service as status finds it, with no overlay running; what the commands answer does not hang on one.
    [Theory]
    [InlineData(TestRecordings.Admin, ServiceState.Running, false, true, null)]
    [InlineData(TestRecordings.NoAdmin, ServiceState.Running, false, true, AgentProblem.DriverMissing)]
    [InlineData(null, ServiceState.Stopped, false, true, AgentProblem.ServiceStopped)]
    [InlineData(null, ServiceState.Stale, false, true, AgentProblem.ServiceStale)]
    [InlineData(null, ServiceState.Stopped, true, true, AgentProblem.PipeTaken)]
    [InlineData(null, ServiceState.Stopped, false, false, AgentProblem.ServiceMissing)]
    public async Task AgentsNeedNoOverlay(string? recording, ServiceState state, bool taken, bool installed, AgentProblem? problem)
    {
        IReadOnlyList<Snapshot> snapshots = recording is null ? [] : [TestRecordings.Read(recording)];
        _rig.Sensors = () => new RecordedFeed(snapshots, state, taken);
        _rig.Installed = installed;
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));

        var status = (await _rig.RunAsync("status")).Json;

        Assert.False((bool)status["overlay"]!);
        Assert.Equal(problem is { } code ? [System.Text.Json.JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString())] : [],
            status["problems"]!.AsArray().Select(each => (string)each!["code"]!));
        if (recording is not null)
        {
            string[][] commands = [["read"], ["sensors"], ["config"], ["edit", """{"kind":"item","item":"cpu.load","field":"name","value":"Load"}"""]];
            foreach (var command in commands)
            {
                var alone = await _rig.RunAsync(command);
                _rig.Overlay = true;
                var beside = await _rig.RunAsync(command);
                _rig.Overlay = false;
                Assert.Equal((0, WithoutTime(alone.Out)), (beside.Exit, WithoutTime(beside.Out)));
            }
        }
    }

    [Fact]
    public async Task ReadSamplesGiveMinMaxAverage()
    {
        // The recording holds the CPU's load and the fans; its tenth second reads nothing, as when every sensor dropped out.
        var series = TestRecordings.Series(TestRecordings.CpuLoad).Select((sample, at) => at == 9 ? new Snapshot([]) : sample).ToList();
        _rig.Sensors = () => new RecordedFeed(series);
        var ids = new[] { "cpu.load", "/lpc/it8689e/0/fan/0", AgentReads.ItemPrefix + "cpu.load" };

        var end = await _rig.RunAsync(["read", "--seconds", "10", .. ids]);

        Assert.Equal(0, end.Exit);
        var config = new ThermiConfig();
        foreach (var (id, value) in ids.Zip(end.Json["values"]!.AsArray()))
        {
            var each = series.Take(10).Select(sample => AgentReads.Read([sample], config, [id]) is { Values: [var one] } ? one.Value : null).OfType<double>().ToList();
            Assert.Equal(9, each.Count);
            Assert.Equal(each.Count, (int)value!["samples"]!);
            Assert.Equal(each.Min(), (double)value["min"]!, 6);
            Assert.Equal(each.Max(), (double)value["max"]!, 6);
            Assert.Equal(each.Average(), (double)value["average"]!, 6);
        }

        Assert.Equal(2, (await _rig.RunAsync("read", "--seconds", "61")).Exit);
    }

    [Fact]
    public async Task AgentEditsAreTheWindowsEdits()
    {
        var snapshot = TestRecordings.Read(TestRecordings.Admin);
        var clock = snapshot.Sensors.First(sensor => sensor.Type == "Clock").Id;
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var texts = Texts.English;
        var expected = new ThermiConfig();
        var edits = new (string Json, Func<ThermiConfig, EditResult> Window)[]
        {
            ("""{"kind":"item","item":"cpu.temperature","field":"name","value":"Tdie"}""",
                PageEdits.Of(new ItemFieldEdit("cpu.temperature", "name", System.Text.Json.JsonSerializer.SerializeToElement("Tdie")), snapshot, texts, () => new HashSet<int>())),
            ("""{"item":"cpu.temperature","warning":70,"critical":90,"kind":"threshold"}""", config => ItemEdits.SetThreshold(config, "cpu.temperature", 70, 90)),
            ("""{"kind":"newOverlay"}""", PageEdits.Of(new NewOverlayEdit(), snapshot, texts, () => new HashSet<int>())),
            ($$"""{"kind":"addItem","overlay":0,"sensor":"{{clock}}"}""", PageEdits.Of(new AddItemEdit(0, clock), snapshot, texts, () => new HashSet<int>())),
        };

        foreach (var (json, window) in edits)
        {
            expected = window(expected).Config;
            Assert.Equal(0, (await _rig.RunAsync("edit", json)).Exit);
            Assert.Equal(ThermiConfigJson.Write(expected), ThermiConfigJson.Write(_rig.Folder.Read()));
        }

        var written = _rig.Folder.Text();
        foreach (var (json, refusal) in new[] { (TooHigh, Refusal.WarningAboveCritical), ("""{"kind":"threshold","item":"nobody","warning":70}""", Refusal.NotFound) })
        {
            var end = await _rig.RunAsync("edit", json);
            Assert.Equal(1, end.Exit);
            Assert.Equal(Texts.Key(refusal), (string)end.Json["refusal"]!);
            Assert.Equal(texts.Of(refusal), (string)end.Json["message"]!);
            Assert.Equal(written, _rig.Folder.Text());
        }
    }

    [Fact]
    public async Task ABrokenFileRefusesAgentEdits()
    {
        const string Broken = "{ \"version\": 3, \"overlays\": [ ";
        _rig.Folder.Write(Broken);

        var end = await _rig.RunAsync("edit", """{"kind":"threshold","item":"cpu.temperature","warning":70,"critical":90}""");

        Assert.Equal(4, end.Exit);
        Assert.NotEmpty(end.Error);
        Assert.Equal(Broken, _rig.Folder.Text());
    }

    private static string WithoutTime(string answer)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(answer)!.AsObject();
        json.Remove("at");
        return json.ToJsonString();
    }
}
