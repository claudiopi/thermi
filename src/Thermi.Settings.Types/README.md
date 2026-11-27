# Thermi.Settings.Types

Writes the settings page's TypeScript from `src/Thermi.Core`, so the page and C# cannot drift apart (`docs/adr/0007-settings-window-in-webview2-and-react.md`). The .NET builds of `src/Thermi.Windows` and `src/Thermi.Linux` run it before the web build (`src/SettingsPage.targets`), which type-checks the page against what it wrote: a change that breaks either side of the bridge fails the build.

What it writes, each only when its text changed, all committed so the page builds without .NET. Never edit them by hand.

| File | From |
| --- | --- |
| `src/Thermi.Settings.Web/src/bridge/bridge.g.ts` | The bridge, `IWindowBridge`, and every type it reaches (`TypeScriptWriter.cs`). |
| `src/Thermi.Settings.Web/src/bridge/texts.g.ts` | The key of every translated text (`TextKeys.cs`). |
| `src/Thermi.Settings.Web/src/bridge/client.g.ts` | The script the window runs before the page, `BridgeCalls.Client`. |
| `docs/agents/schema` | The JSON Schema of each document of the agents' contract, `AgentSchemas.Current`. |

`Program.cs` is the entry point. Run it by hand with `dotnet run --project src/Thermi.Settings.Types -- src/Thermi.Settings.Web/src/bridge docs/agents/schema`.
