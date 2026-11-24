using System.Text;
using Thermi.Core.Bridge;
using Thermi.Settings.Types;

// One argument, the folder the page's generated files go to. A file is written only when its text changed, so the web
// build after it sees no change it did not make.
if (args is not [var folder])
{
    await Console.Error.WriteLineAsync("thermi-settings-types <folder>: writes bridge.g.ts, texts.g.ts and client.g.ts into the folder.");
    return 2;
}

Directory.CreateDirectory(folder);
await WriteAsync(Path.Combine(folder, "bridge.g.ts"), new TypeScriptWriter().Bridge(typeof(IWindowBridge)));
await WriteAsync(Path.Combine(folder, "texts.g.ts"), TextKeys.Write());
// The script the host runs before the page, so the page's tests hold the wire the real window uses.
var client = System.Text.Json.JsonSerializer.Serialize(BridgeCalls.Client);
await WriteAsync(Path.Combine(folder, "client.g.ts"), $"{TypeScriptWriter.Header}export const client = {client};\n");
return 0;

static async Task WriteAsync(string path, string text)
{
    if (!File.Exists(path) || await File.ReadAllTextAsync(path) != text)
    {
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
