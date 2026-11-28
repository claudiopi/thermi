# Thermi.Settings.Types

Writes the settings page's TypeScript from `src/Thermi.Core`, so the page and C# cannot drift apart (`docs/adr/0007-settings-window-in-webview2-and-react.md`). Its own build runs it, then installs the page when its lockfile changed and builds it, which type-checks the page against what it wrote: a change that breaks either side of the bridge fails the build. `src/Thermi.Windows` and `src/Thermi.Linux` both reference it and only take the page's `dist` (`src/SettingsPage.targets`): MSBuild builds a project once per build, so npm runs once in the page's folder, where two at once fail on each other's files.

What it writes, each only when its text changed, all committed so the page builds without .NET. Never edit them by hand.

| File | From |
| --- | --- |
| `src/Thermi.Settings.Web/src/bridge/bridge.g.ts` | The bridge, `IWindowBridge`, and every type it reaches (`TypeScriptWriter.cs`). |
| `src/Thermi.Settings.Web/src/bridge/texts.g.ts` | The key of every translated text (`TextKeys.cs`). |
| `src/Thermi.Settings.Web/src/bridge/client.g.ts` | The script the window runs before the page, `BridgeCalls.Client`, and the one Linux's window hands the page each message with, `BridgeCalls.Dispatched`. |
| `docs/agents/schema` | The JSON Schema of each document of the agents' contract, `AgentSchemas.Current`. |

`Program.cs` is the entry point. Run it by hand with `dotnet run --project src/Thermi.Settings.Types -- src/Thermi.Settings.Web/src/bridge docs/agents/schema`.
