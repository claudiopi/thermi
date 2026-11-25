using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Thermi.Core.Editing;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
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

            var (_, config) = await session.CallAsync("config");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse((await _rig.RunAsync("config")).Out), JsonNode.Parse(config)));
            var (_, read) = await session.CallAsync("read", new() { ["ids"] = new[] { "cpu.load" } });
            Assert.True(JsonNode.DeepEquals(WithoutTime((await _rig.RunAsync("read", "cpu.load")).Json), WithoutTime(McpSession.Answer(read))));

            var tooHigh = McpSession.Json("""{"kind":"threshold","item":"cpu.temperature","warning":95,"critical":90}""");
            var (refused, why) = await session.CallAsync("edit", new() { ["edit"] = tooHigh });
            Assert.True(refused);
            Assert.Contains(Core.Translations.Texts.Key(Refusal.WarningAboveCritical), why, StringComparison.Ordinal);

            _rig.Sensors = () => new RecordedFeed([], ServiceState.Stopped);
            var (unread, said) = await session.CallAsync("read");
            Assert.True(unread);
            Assert.NotEmpty(said);
        });

        // The built door, over its stdio, on a desktop of its own; monitors reads no settings.
        using var process = DesktopProcess.Start(Path.Combine(AppContext.BaseDirectory, "thermi-cli.exe"), ["mcp"], talks: true);
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
        await McpSession.RunAsync(_rig, async session =>
        {
            Assert.False((await session.CallAsync("edit", Name("cpu.temperature", "Agent's"))).Error);
            Assert.False((await session.CallAsync("edit", Name("cpu.load", "Agent's too"))).Error);
            _rig.Folder.Write(ThermiConfigJson.Write(ItemEdits.Rename(_rig.Folder.Read(), "cpu.load", "By hand").Config));

            Assert.False((await session.CallAsync("undo")).Error);
            Assert.False((await session.CallAsync("undo")).Error);
            Assert.Equal((null, "By hand"), (NameOf("cpu.temperature"), NameOf("cpu.load")));

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
