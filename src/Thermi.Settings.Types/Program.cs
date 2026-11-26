using System.Text;
using Thermi.Core.Bridge;
using Thermi.Settings.Types;

// The folder the page's generated files go to, and the one the agents' contract keeps its schemas in. A file is written
// only when its text changed, so the web build after it sees no change it did not make, and with the line ends git checks
// out (.gitattributes), so a build leaves git clean.
if (args is not [var folder, var schemas])
{
    await Console.Error.WriteLineAsync(
        "thermi-settings-types <folder> <schemas>: writes bridge.g.ts, texts.g.ts and client.g.ts into the first, "
            + "and the agents' JSON Schemas into the second.");
    return 2;
}

Directory.CreateDirectory(folder);
await WriteAsync(Path.Combine(folder, "bridge.g.ts"), new TypeScriptWriter().Bridge(typeof(IWindowBridge)));
await WriteAsync(Path.Combine(folder, "texts.g.ts"), TextKeys.Write());
// The script the host runs before the page, so the page's tests hold the wire the real window uses.
var client = System.Text.Json.JsonSerializer.Serialize(BridgeCalls.Client);
await WriteAsync(Path.Combine(folder, "client.g.ts"), $"{TypeScriptWriter.Header}export const client = {client};\n");
Directory.CreateDirectory(schemas);
var indented = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
foreach (var (name, schema) in Thermi.Core.Agents.AgentSchemas.Current)
{
    await WriteAsync(Path.Combine(schemas, $"{name}.schema.json"), schema.ToJsonString(indented) + "\n");
}

return 0;

static async Task WriteAsync(string path, string written)
{
    var text = written.ReplaceLineEndings("\r\n");
    if (!File.Exists(path) || await File.ReadAllTextAsync(path) != text)
    {
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
