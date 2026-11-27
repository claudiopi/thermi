using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Tests.Agents;
using Thermi.Tests.Contract;

namespace Thermi.Tests.Acceptance;

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
        Unpainted,
    }

    // The ways each command ends that no test of its own already holds: an edit's 0, 1 and 4, a look's, a read's past
    // 60 seconds, the start at sign-in's, monitors' and looks' 0, status's with the service stopped and a write's on a
    // broken file are theirs. A picture the host could not paint is said on stderr alone, as an elevated write's refusal.
    [Theory]
    [InlineData(Ending.Done, 0, "status", "--schema", "1")]
    [InlineData(Ending.Done, 2, "read", "no.such.metric")]
    [InlineData(Ending.Done, 2, "watch", "no.such.metric")]
    [InlineData(Ending.Done, 2, "edit", "not json")]
    [InlineData(Ending.Done, 2, "status", "--schema", "2")]
    [InlineData(Ending.Done, 2, "preview", "--overlay", "1")]
    [InlineData(Ending.Done, 2, "preview", "--scale", "1,5")]
    [InlineData(Ending.Done, 2, "preview", "--scale", "0.2")]
    [InlineData(Ending.Done, 2, "preview", "--scale", "8.5")]
    [InlineData(Ending.Unpainted, 1, "preview")]
    [InlineData(Ending.Stopped, 3, "read")]
    [InlineData(Ending.Stopped, 3, "sensors")]
    [InlineData(Ending.Stopped, 3, "preview")]
    [InlineData(Ending.Broken, 4, "config")]
    [InlineData(Ending.Broken, 4, "preview")]
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

        _rig.Unpaintable = ending == Ending.Unpainted;
        var end = await _rig.RunAsync(args);

        Assert.Equal(exit, end.Exit);
        Assert.Equal(exit == 0, end.Error.Length == 0);
        if (end.Out.Length > 0)
        {
            Assert.Single(end.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(AgentSchema.Current, (int)end.Json["schema"]!);
        }

        Assert.Equal((exit is 0 or 1 && ending != Ending.Unpainted) || args[0] == "config", end.Out.Length > 0);
    }

    // From an elevated terminal, a command that may write the settings, a look, the finder's mark or the start at sign-in
    // is refused before it writes anything, as a link in the user's folders could send an elevated write anywhere; one
    // that only reads still answers.
    [Theory] // Rows: each one operand of Writes or its other side, which Stryker makes no mutant of; a security check holds each
    [InlineData(true, "edit", """{"kind":"item","item":"cpu.load","field":"name","value":"Load"}""")]
    [InlineData(true, "look", "save", "Mine", "--overlay", "0")]
    [InlineData(true, "start-with-windows", "on")]
    [InlineData(true, "mcp")]
    [InlineData(false, "start-with-windows")]
    public async Task AnElevatedCliOnlyReads(bool writes, params string[] args)
    {
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var before = _rig.Folder.Text();
        _rig.Raised = true;

        var end = await _rig.RunAsync(args);

        Assert.Equal((writes ? 1 : 0, writes, before), (end.Exit, end.Out.Length == 0, _rig.Folder.Text()));
    }

    // The service as status finds it, with no overlay running, or a per-user install's own host, which runs only while thermi
    // does; what the commands answer does not hang on an overlay.
    [Theory] // Rows: each an arm of the status's problems, whose answer Stryker makes no mutant of
    [InlineData(TestRecordings.Admin, ServiceState.Running, false, true, null)]
    [InlineData(TestRecordings.NoAdmin, ServiceState.Running, false, true, AgentProblem.DriverMissing)]
    [InlineData(null, ServiceState.Stopped, false, true, AgentProblem.ServiceStopped)]
    [InlineData(null, ServiceState.Stale, false, true, AgentProblem.ServiceStale)]
    [InlineData(null, ServiceState.Stopped, true, true, AgentProblem.PipeTaken)]
    [InlineData(null, ServiceState.Stopped, false, false, AgentProblem.ServiceMissing)]
    [InlineData(TestRecordings.NoAdmin, ServiceState.Running, false, false, AgentProblem.DriverMissing, true)]
    [InlineData(null, ServiceState.Stopped, false, false, AgentProblem.NotRunning, true)]
    public async Task AgentsNeedNoOverlay(string? recording, ServiceState state, bool taken, bool installed, AgentProblem? problem, bool perUser = false)
    {
        _rig.PerUser = perUser;
        IReadOnlyList<Snapshot> snapshots = recording is null ? [] : [TestRecordings.Read(recording)];
        _rig.Sensors = () => new RecordedFeed(snapshots, state, taken);
        _rig.Installed = installed;
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));

        var asked = await _rig.RunAsync("status");
        Assert.Equal((0, ""), (asked.Exit, asked.Error));
        var status = asked.Json;

        Assert.False((bool)status["overlay"]!);
        Assert.Equal(problem is { } code ? [System.Text.Json.JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString())] : [],
            status["problems"]!.AsArray().Select(each => (string)each!["code"]!));

        // With the sensors alone, there is no menu or window to send the person to, so a fix that names one says otherwise.
        _rig.SensorsOnly = true;
        var sensorsOnly = (await _rig.RunAsync("status")).Json;
        _rig.SensorsOnly = false;
        Assert.Equal(
            problem is AgentProblem.ServiceStopped or AgentProblem.DriverMissing,
            (string?)sensorsOnly["problems"]!.AsArray().FirstOrDefault()?["fix"] != (string?)status["problems"]!.AsArray().FirstOrDefault()?["fix"]);
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

    // The host paints; the command chooses what: the overlay asked, of the settings as read, for the snapshot of now, at the
    // scale asked, or at the largest that keeps it within the longest side an agent gets, which an overlay of the largest
    // size at the largest scale is past. It only reads, so agents that only read see it too.
    [Theory]
    [InlineData(2, 1.5f, 1.5f)]
    [InlineData(4, 8f, 5f)]
    public async Task AgentsSeeTheOverlay(double size, float asked, float used)
    {
        var two = OverlayEdits.Add(new ThermiConfig()).Config;
        var config = two with { Overlays = [two.Overlays[0], two.Overlays[1] with { Size = size }], Agents = AgentAccess.Read };
        _rig.Folder.Write(ThermiConfigJson.Write(config));

        var end = await _rig.RunAsync("preview", "--overlay", "1", "--scale", asked.ToString(CultureInfo.InvariantCulture));

        var painted = AgentRig.Painted(config, 1, TestRecordings.Read(TestRecordings.Admin), used);
        Assert.InRange(Math.Max(painted.Width, painted.Height), 1, 2000);
        Assert.Equal(
            (0, 1, used, painted.Width, painted.Height, Convert.ToBase64String(painted.Png)),
            (end.Exit, (int?)end.Json["overlay"], (float?)end.Json["scale"], (int?)end.Json["width"], (int?)end.Json["height"], (string?)end.Json["png"]));
        var schema = Path.Combine(TestFiles.Root, "docs", "agents", "schema", "preview.schema.json");
        Assert.Empty(SchemaCheck.Findings(JsonNode.Parse(await File.ReadAllTextAsync(schema, TestContext.Current.CancellationToken))!, end.Json, "preview"));
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
            var each = series.Take(10)
                .Select(sample => AgentReads.Read([sample], config, [id]) is { Values: [var one] } ? one.Value : null).OfType<double>().ToList();
            Assert.Equal(9, each.Count);
            Assert.Equal(each.Count, (int)value!["samples"]!);
            Assert.Equal(each.Min(), (double)value["min"]!, 6);
            Assert.Equal(each.Max(), (double)value["max"]!, 6);
            Assert.Equal(each.Average(), (double)value["average"]!, 6);
        }

        Assert.Equal((2, 2), ((await _rig.RunAsync("read", "--seconds", "0")).Exit, (await _rig.RunAsync("read", "--seconds", "61")).Exit));
    }

    // A watch of the same series, with stdin kept open: a line each second, the sensor that dropped out of one read as
    // missing, and once the service goes quiet, its end with why.
    [Fact]
    public async Task AWatchGoesOnUntilTheServiceGoesQuiet()
    {
        var series = TestRecordings.Series(TestRecordings.CpuLoad).Take(12).Select((sample, at) => at == 9 ? new Snapshot([]) : sample).ToList();
        _rig.Sensors = () => new RecordedFeed(series, pace: _rig.Folder.Clock);

        var end = await _rig.RunAsync("watch", "/lpc/it8689e/0/fan/0");

        var lines = end.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!["values"]![0]!).ToList();
        Assert.Equal((3, series.Count), (end.Exit, lines.Count));
        Assert.Equal([.. Enumerable.Range(0, series.Count).Select(at => at == 9)], lines.Select(value => value["value"] is null));
        Assert.Equal("reads-nothing", (string)lines[9]["why"]!);
        Assert.NotEmpty(end.Error);
    }

    // Core decides each edit as the window does (AgentEditTests); here, what is written to the file, and that a refusal
    // writes nothing.
    [Fact]
    public async Task AgentEditsWriteTheFileOrLeaveIt()
    {
        // An item added from the service's reading, so the edit reads it as the window does.
        var snapshot = TestRecordings.Read(TestRecordings.Admin);
        var clock = snapshot.Sensors.First(sensor => sensor.Type == "Clock").Id;
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var added = PageEdits.Of(new AddItemEdit(0, clock), snapshot, Texts.English, () => new HashSet<int>())(new ThermiConfig()).Config;

        Assert.Equal(0, (await _rig.RunAsync("edit", $$"""{"kind":"addItem","overlay":0,"sensor":"{{clock}}"}""")).Exit);
        Assert.Equal(ThermiConfigJson.Write(added), ThermiConfigJson.Write(_rig.Folder.Read()));

        var written = _rig.Folder.Text();
        Assert.Equal(1, (await _rig.RunAsync("edit", TooHigh)).Exit);
        Assert.Equal(written, _rig.Folder.Text());
    }

    // The overlay's own writes, as a drop, go out through its throttle as changes of whatever the file holds then, and an
    // agent's edit is made over the file as it is then: neither loses the other's, and the overlay shows the agent's.
    [Fact]
    public async Task AgentEditsKeepOthersChanges()
    {
        var folder = _rig.Folder;
        await using var overlay = await folder.LoadedAsync(new ThermiConfig());
        using var watcher = overlay.Watch(folder.Log)!;
        for (var round = 0; round < 10; round++)
        {
            var dropped = new Position(10 * round, 20);
            overlay.Edit(config => OverlayEdits.Drop(config, 0, dropped).Config);
            var agent = _rig.RunAsync("edit", $$"""{"kind":"threshold","item":"cpu.temperature","warning":{{60 + round}},"critical":95}""");
            var sync = overlay.SyncAsync();
            await folder.StepWhileAsync(sync);
            await sync;
            Assert.Equal(0, (await agent).Exit);
            var after = overlay.SyncAsync();
            await folder.StepWhileAsync(after);
            await after;

            var written = folder.Read().Overlays[0];
            Assert.Equal((dropped, 60.0 + round), (written.Position!, written.Items[0].Threshold!.Warning!.Value));
        }

        var before = ThermiConfigJson.Write(folder.Read());
        Assert.Equal(0, (await _rig.RunAsync("edit", """{"kind":"item","item":"cpu.load","field":"name","value":"Agent's"}""")).Exit);
        // The change is in the log, and the settings as they were stay beside them, so a person can see it and take it back.
        Assert.Contains(folder.Log.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("Agent's", StringComparison.Ordinal));
        // An edit that changes nothing keeps the copy of the last one that did.
        Assert.Equal(0, (await _rig.RunAsync("edit", """{"kind":"item","item":"cpu.load","field":"name","value":"Agent's"}""")).Exit);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.ChangeExtension(folder.File, ".before-agent.json"), TestContext.Current.CancellationToken));
        var shown = false;
        for (var waited = TimeSpan.Zero; waited <= TimeSpan.FromSeconds(1) && !shown; waited += Settings.Interval)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, TestContext.Current.CancellationToken);
            folder.Clock.Advance(Settings.Interval);
            shown = overlay.Config.Overlays[0].Items.Any(item => item.Name == "Agent's");
        }

        Assert.True(shown, "The overlay did not show the agent's edit within a second.");
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

        // With the sensors alone, no settings window offers to restore the file, so the fix says otherwise.
        var full = (await _rig.RunAsync("status")).Json["problems"]!.AsArray().Single(each => (string?)each!["code"] == "settings-broken")!;
        _rig.SensorsOnly = true;
        var alone = (await _rig.RunAsync("status")).Json["problems"]!.AsArray().Single(each => (string?)each!["code"] == "settings-broken")!;
        _rig.SensorsOnly = false;
        Assert.NotEqual((string?)full["fix"], (string?)alone["fix"]);

        // A file whose mutex another thermi process holds through every try is the same: nothing written, and 4.
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var written = _rig.Folder.Text();
        using (MutexHolder.Holding(_rig.Folder.MutexName))
        {
            Assert.Equal(4, (await _rig.RunAsync("edit", """{"kind":"threshold","item":"cpu.temperature","warning":70}""")).Exit);
        }

        Assert.Equal(written, _rig.Folder.Text());
    }

    private static string WithoutTime(string answer)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(answer)!.AsObject();
        json.Remove("at");
        return json.ToJsonString();
    }
}
