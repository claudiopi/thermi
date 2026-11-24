# Thermi.Settings.Types

Writes the settings window page's TypeScript from `src/Thermi.Core`, so the page and C# cannot drift apart (`docs/adr/0007-settings-window-in-webview2-and-react.md`). The .NET build of `src/Thermi` runs it before the web build, which type-checks the page against what it wrote; a change that breaks either side of the bridge fails that build.

- `TypeScriptWriter.cs` writes `src/Thermi.Settings.Web/src/bridge/bridge.g.ts`: the bridge, `IWindowBridge` in `src/Thermi.Core/Bridge`, and every type it reaches, as the bridge's JSON carries them.
- `TextKeys.cs` writes `src/Thermi.Settings.Web/src/bridge/texts.g.ts`: the key of every text, `Enum.Member` of Core's translated enums. The texts themselves reach the page through the bridge, in the window's language.

- `Program.cs` writes `src/Thermi.Settings.Web/src/bridge/client.g.ts`: the script the host runs before the page, `BridgeCalls.Client`, as text, so the page's unit tests hold the wire the real window uses.

A file is written only when its text changed. All are committed, so the page builds and its tests run without .NET; never edit them by hand.

Run it by hand with `dotnet run --project src/Thermi.Settings.Types -- src/Thermi.Settings.Web/src/bridge`.
