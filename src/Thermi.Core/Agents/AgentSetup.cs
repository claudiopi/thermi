using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Thermi.Core.Agents;

/// <summary>
/// How a person adds thermi to an agent, as the settings window's AI agents section shows it: the command for Claude Code,
/// and the JSON for a client set up by a file, as Claude Desktop, Cursor or VS Code. Both start <c>thermi-cli mcp</c>, by
/// its name where its folder is on the <c>PATH</c>, else by its full path, which a client started before thermi was
/// installed also finds.
/// </summary>
public static class AgentSetup
{
    public const string Program = "thermi-cli";

    // A path's backslashes and any letter stay as they are: the person pastes this into a file of their own.
    private static readonly JsonSerializerOptions Indented =
        new() { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><paramref name="cli"/>, the full path of <c>thermi-cli</c>, as a client starts it with the folders of <paramref name="path"/>.</summary>
    public static string Command(string cli, IEnumerable<string> path)
    {
        var folder = Folder(Path.GetDirectoryName(cli) ?? "");
        return path.Any(each => folder.Length > 0 && string.Equals(Folder(each), folder, StringComparison.OrdinalIgnoreCase)) ? Program : cli;
    }

    public static string ClaudeCode(string command) =>
        $"claude mcp add thermi -- {(command.Contains(' ', StringComparison.Ordinal) ? $"\"{command}\"" : command)} mcp";

    public static string File(string command) =>
        new JsonObject { ["mcpServers"] = new JsonObject { ["thermi"] = new JsonObject { ["command"] = command, ["args"] = new JsonArray("mcp") } } }
            .ToJsonString(Indented);

    // A folder of the PATH as Windows compares it: quotes and a trailing separator are no part of it.
    private static string Folder(string folder) => folder.Trim().Trim('"').TrimEnd('\\', '/');
}
