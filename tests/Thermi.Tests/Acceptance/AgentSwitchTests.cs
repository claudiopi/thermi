using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Tests.Agents;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The person's say over what agents do (<c>docs/specs/003-agents/spec.md</c>, R30 to R32): a switch that lets them only
/// read, which no agent turns back on, and the window's AI agents section, which says how to connect one and what they did.
/// </summary>
public sealed class AgentSwitchTests : IDisposable
{
    private const string Rename = """{"kind":"item","item":"cpu.load","field":"name","value":"Load"}""";

    private readonly AgentRig _rig = new();

    public void Dispose() => _rig.Dispose();

    // Every command that would write something, the settings, a look's file, the start at sign-in or the finder's load, is
    // refused before it does while agents only read, and so are an MCP session's edit and undo; an agent may stop agents
    // changing anything, never let them.
    [Theory]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "edit", Rename)]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "look", "save", "Mine", "--overlay", "0")]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "look", "wear", "Calm")]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "start-with-windows", "on")]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "find-cpu-fan")]
    [InlineData(AgentAccess.Read, Refusal.AgentsReadOnly, "mcp")]
    [InlineData(AgentAccess.Change, Refusal.AgentsOnlyByPerson, "edit", """{"kind":"thermi","field":"agents","value":"change"}""")]
    [InlineData(AgentAccess.Change, null, "edit", """{"kind":"thermi","field":"agents","value":"read"}""")]
    public async Task AgentsOnlyReadWhenThePersonSaysSo(AgentAccess access, Refusal? refusal, params string[] args)
    {
        var looks = new SavedStyles(ConfigStore.StylesFolder(_rig.Folder.File));
        Assert.Null(looks.Save(new StyleConfig(Gap: 12), "Calm"));
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig { Agents = access }));
        var before = _rig.Folder.Text();

        if (args is ["mcp"])
        {
            await RefusedOverMcpAsync();
        }
        else
        {
            var end = await _rig.RunAsync(args);

            Assert.Equal(refusal is null ? 0 : 1, end.Exit);
            Assert.Equal(refusal is { } key ? Texts.Key(key) : null, (string?)end.Json["refusal"]);
            Assert.Equal(refusal is { } said ? Texts.English.All[Texts.Key(said)] : "", end.Error.Trim());
        }

        Assert.Equal(refusal is null ? AgentAccess.Read : access, ThermiConfigJson.Read(_rig.Folder.Text()).Config.Agents);
        Assert.Equal(refusal is not null, before == _rig.Folder.Text());
        Assert.Equal((false, 0, 1), (_rig.Startup.On, _rig.Loads.Count, looks.List().Count));
        Assert.Equal(0, (await _rig.RunAsync("config")).Exit);
    }

    // The settings window's section on a log thermi-cli wrote: six edits, one refused, and an undo; thermi-cli by its name
    // where its folder is on the PATH, else by its full path, which a client takes quoted for its space.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task TheWindowSaysHowToConnectAndWhatAgentsDid(bool onPath, bool acted)
    {
        var folder = Path.Combine(Path.GetDirectoryName(_rig.Folder.File)!, "Program Files", "thermi");
        var log = Path.Combine(Path.GetDirectoryName(_rig.Folder.File)!, "thermi-agents.log");
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        string[] edits = [.. Enumerable.Range(1, 6).Select(each => Rename.Replace("\"Load\"", $"\"Load {each}\"", StringComparison.Ordinal))];
        if (acted)
        {
            await ActAsync(log, edits);
        }

        var command = AgentSetup.Command(Path.Combine(folder, "thermi-cli.exe"), onPath ? [Path.GetTempPath(), folder + @"\"] : [Path.GetTempPath()]);
        var view = AgentActivity.View(
            command,
            AgentActivity.Changes(AgentsLog.Lines(log)),
            new ValueFormat(Texts.English, CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc);

        var named = onPath ? "thermi-cli" : $"{folder}\\thermi-cli.exe";
        Assert.Equal($"claude mcp add thermi -- {(onPath ? named : $"\"{named}\"")} mcp", view.Command);
        Assert.Equal(named, (string?)JsonNode.Parse(view.File)!["mcpServers"]!["thermi"]!["command"]);
        Assert.Equal("mcp", (string?)JsonNode.Parse(view.File)!["mcpServers"]!["thermi"]!["args"]![0]);
        Assert.Equal(
            acted ? [Texts.English.Of(AgentDid.Undo), .. Enumerable.Repeat(Texts.English.Of(AgentDid.Edit), 4)] : [],
            view.Changes.Select(change => change.Did));
        Assert.Equal(acted ? edits[2..].Reverse() : [], view.Changes.Skip(1).Select(change => change.What));
        Assert.All(view.Changes, change => Assert.NotEmpty(change.When));
    }

    private Task RefusedOverMcpAsync() => McpSession.RunAsync(_rig, async session =>
    {
        foreach (var (tool, arguments) in new (string, Dictionary<string, object?>)[] { ("edit", new() { ["edit"] = McpSession.Json(Rename) }), ("undo", []) })
        {
            var (error, why) = await session.CallAsync(tool, arguments);
            Assert.True(error);
            Assert.Contains(Texts.Key(Refusal.AgentsReadOnly), why, StringComparison.Ordinal);
        }

        Assert.False((await session.CallAsync("config")).Error);
    });

    // Each command as its own process would write it to the agents' log, then a session's undo of the last.
    private async Task ActAsync(string log, string[] edits)
    {
        using (var logs = ThermiLog.Create(log, _rig.Folder.Clock, trace: false))
        {
            _rig.Log = logs.CreateLogger("Thermi.Cli");
            foreach (var edit in edits[..^1])
            {
                Assert.Equal(0, (await _rig.RunAsync("edit", edit)).Exit);
            }

            Assert.Equal(1, (await _rig.RunAsync("edit", """{"kind":"threshold","item":"cpu.temperature","warning":95,"critical":90}""")).Exit);
            using var commands = new AgentCommands(_rig.Environment, _rig.Folder.Clock);
            foreach (var step in new Func<Task<AgentReply>>[]
            {
                () => commands.RunAsync(new AgentRequest(AgentVerb.Edit) { Text = edits[^1] }, TestContext.Current.CancellationToken),
                () => commands.UndoAsync(TestContext.Current.CancellationToken),
            })
            {
                var run = step();
                await _rig.Folder.StepWhileAsync(run);
                Assert.Equal(AgentExit.Done, (await run).Exit);
            }
        }

        _rig.Log = null;
    }
}
