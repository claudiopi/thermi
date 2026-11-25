using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Windows.Agents;
using Thermi.Windows.Commands;
using Thermi.Windows.Overlay;
using Thermi.Windows.Tests.Agents;
using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// <c>thermi-cli mcp</c> as an agent's client sees it (<c>docs/specs/003-agents/spec.md</c>): the commands as tools, each
/// answering the command's JSON, refusals as tool errors, and an undo that takes back only the session's own edits.
/// </summary>
public sealed class AgentMcpTests : IDisposable
{
    private static readonly string[] ReadOnly = ["status", "read", "sensors", "config", "monitors", "looks"];

    private static readonly string[] Changing = ["edit", "look", "find_cpu_fan", "start_with_windows", "undo", "redo"];

    private readonly AgentRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task McpToolsAreTheCommands()
    {
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        await McpSession.RunAsync(_rig, async session =>
        {
            var tools = await session.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(ReadOnly.Concat(Changing).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
            Assert.Equal(
                ReadOnly.Order(StringComparer.Ordinal),
                tools.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(tool => tool.Name).Order(StringComparer.Ordinal));
            // Destructive where undo cannot take it back; a hint left out means destructive, so every other tool says it is not.
            Assert.Equal(
                ["edit", "look", "start_with_windows"],
                tools.Where(tool => tool.ProtocolTool.Annotations?.DestructiveHint != false).Select(tool => tool.Name).Order(StringComparer.Ordinal));
            Assert.Equal(
                ReadOnly.Append("start_with_windows").Order(StringComparer.Ordinal),
                tools.Where(tool => tool.ProtocolTool.Annotations?.IdempotentHint == true).Select(tool => tool.Name).Order(StringComparer.Ordinal));

            var (_, config) = await session.CallAsync("config");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse((await _rig.RunAsync("config")).Out), JsonNode.Parse(config)));
            var (_, read) = await session.CallAsync("read", new() { ["ids"] = new[] { "cpu.load" } });
            Assert.True(JsonNode.DeepEquals(WithoutTime((await _rig.RunAsync("read", "cpu.load")).Json), WithoutTime(McpSession.Answer(read))));

            var (_, sensors) = await session.CallAsync("sensors", new() { ["types"] = new[] { "temperature" } });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse((await _rig.RunAsync("sensors", "--type", "temperature")).Out), JsonNode.Parse(sensors)));
            Assert.True((await session.CallAsync("read", new() { ["seconds"] = 61 })).Error);

            var tooHigh = McpSession.Json("""{"kind":"threshold","item":"cpu.temperature","warning":95,"critical":90}""");
            var (refused, why) = await session.CallAsync("edit", new() { ["edit"] = tooHigh });
            Assert.True(refused);
            Assert.Contains(Core.Translations.Texts.Key(Refusal.WarningAboveCritical), why, StringComparison.Ordinal);

            // The tools with their own arguments answer as their commands do.
            var (_, exported) = await session.CallAsync("look", new() { ["action"] = "export", ["overlay"] = 0 });
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse((await _rig.RunAsync("look", "export", "--overlay", "0")).Out), JsonNode.Parse(exported)));
            var (_, start) = await session.CallAsync("start_with_windows");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse((await _rig.RunAsync("start-with-windows")).Out), JsonNode.Parse(start)));

            _rig.Sensors = () => new RecordedFeed([], ServiceState.Stopped);
            var (unread, said) = await session.CallAsync("read");
            Assert.True(unread);
            Assert.NotEmpty(said);
        });

        // The built door, over its stdio, on a desktop of its own; monitors reads no settings.
        using var process = DesktopProcess.Start(Path.Combine(AppContext.BaseDirectory, "thermi-cli.exe"), ["mcp"], _rig.Folder.Profile, talks: true);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(process.Input.BaseStream, process.Output.BaseStream),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(12, (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Count);
        var monitors = await client.CallToolAsync("monitors", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(monitors.IsError ?? false);
    }

    [Fact]
    public async Task McpUndoTakesBackOnlyItsOwn()
    {
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));

        // Changes go one at a time: an undo called while a look is being worn, the file's mutex held, waits for it and takes it back.
        Assert.Null(new SavedStyles(ConfigStore.StylesFolder(_rig.Folder.File)).Save(new StyleConfig(Gap: 12), "Calm"));
        using (var commands = new AgentCommands(_rig.Environment, _rig.Folder.Clock))
        {
            Task<AgentReply> wear, undo;
            using (MutexHolder.Holding(_rig.Folder.MutexName))
            {
                wear = commands.RunAsync(new AgentRequest(AgentVerb.Look) { Look = LookAction.Wear, Text = "Calm" }, TestContext.Current.CancellationToken);
                undo = commands.UndoAsync(TestContext.Current.CancellationToken);
            }

            for (var step = 0; !(wear.IsCompleted && undo.IsCompleted) && step < 2000; step++)
            {
                _rig.Folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
                await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
            }

            Assert.Equal(AgentExit.Done, (await wear).Exit);
            Assert.True((bool)JsonNode.Parse((await undo).Json!)!["done"]!);
            Assert.Equal(ThermiConfigJson.Write(new ThermiConfig()), ThermiConfigJson.Write(_rig.Folder.Read()));

            // An edit that waits for a silent service holds up no other change: the redo goes first.
            _rig.Sensors = () => new RecordedFeed([], ServiceState.Running);
            var named = """{"kind":"item","item":"cpu.temperature","field":"name","value":"Late"}""";
            var edit = commands.RunAsync(new AgentRequest(AgentVerb.Edit) { Text = named }, TestContext.Current.CancellationToken);
            var redo = commands.RedoAsync(TestContext.Current.CancellationToken);
            for (var step = 0; !redo.IsCompleted && step < 2000; step++)
            {
                _rig.Folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
                await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
            }

            Assert.False(edit.IsCompleted);
            for (var step = 0; !edit.IsCompleted && step < 2000; step++)
            {
                _rig.Folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
                await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
            }

            await edit;
            _rig.Sensors = () => new RecordedFeed([TestRecordings.Read(TestRecordings.Admin)]);
        }

        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));

        await McpSession.RunAsync(_rig, async session =>
        {
            // Two edits of one field are two steps, however soon the second comes.
            Assert.False((await session.CallAsync("edit", Name("cpu.temperature", "First"))).Error);
            Assert.False((await session.CallAsync("edit", Name("cpu.temperature", "Agent's"))).Error);
            Assert.False((await session.CallAsync("edit", Name("cpu.load", "Agent's too"))).Error);
            _rig.Folder.Write(ThermiConfigJson.Write(ItemEdits.Rename(_rig.Folder.Read(), "cpu.load", "By hand").Config));

            // An undo the file's lock kept from being written is still the next one.
            using (MutexHolder.Holding(_rig.Folder.MutexName))
            {
                Assert.True((await session.CallAsync("undo")).Error);
            }

            var (_, skipped) = await session.CallAsync("undo");
            Assert.False((bool)McpSession.Answer(skipped)["done"]!);
            Assert.False((await session.CallAsync("undo")).Error);
            Assert.Equal(("First", "By hand"), (NameOf("cpu.temperature"), NameOf("cpu.load")));

            Assert.False((await session.CallAsync("redo")).Error);
            Assert.Equal(("Agent's", "By hand"), (NameOf("cpu.temperature"), NameOf("cpu.load")));
        });

        static Dictionary<string, object?> Name(string item, string name)
        {
            return new() { ["edit"] = McpSession.Json($$"""{"kind":"item","item":"{{item}}","field":"name","value":"{{name}}"}""") };
        }
    }

    private static JsonObject WithoutTime(JsonObject answer)
    {
        answer.Remove("at");
        return answer;
    }

    private string? NameOf(string item) => _rig.Folder.Read().Overlays.SelectMany(overlay => overlay.Items).Single(each => each.Id == item).Name;
}
