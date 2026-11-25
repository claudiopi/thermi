# 003. AI agents read the sensors and configure thermi

**Status**: draft

## Why

thermi exists because AI agents running for hours cooked their owner's PC. The agent itself should be able to ask how hot the PC is, back off when it runs hot, and set thermi up for the person, from a command line or as an MCP server, with or without an overlay on screen. The decisions are in `docs/adr/0013-agents-cli-and-mcp.md`.

## Requirements

The door

- R1. `thermi-cli` runs from PowerShell, cmd and Git Bash once thermi is installed: the shell waits for it, it prints to that shell, and it exits with its code. With no command it prints its help; it never starts an overlay.
- R2. Every command prints one JSON document on stdout, with `"schema": 1`, and nothing else; errors and help go to stderr. Exit codes: 0 done, 1 refused by thermi's rules, 2 bad arguments, 3 the sensor service unreachable, stopped or stale, 4 the settings file broken.
- R3. Every command works the same with no overlay and no settings window running.
- R4. Nothing `thermi-cli` does runs elevated, asks for administrator rights, starts the sensor service or installs the driver.

Reading

- R5. `status` says whether the sensor service runs and its data is fresh, whether the driver looks missing, whether an overlay runs, and why the settings file cannot be used, if it cannot.
- R6. `read` with no ids gives every metric and every item of every overlay; with ids, those alone, each a metric (`cpu.temperature`), a sensor id or an item id. A metric reads as the first overlay maps it, the CPU fan it was given included. Each value has its unit, and one that cannot be read is null with why, never 0.
- R7. `read --seconds n`, n from 1 to 60, samples once a second for n seconds and gives each value's minimum, maximum, average and number of samples, beside the last.
- R8. Each item read gives its level as the overlay judges it: normal, warning or critical, with the thresholds that decide it.
- R9. `sensors` lists every sensor the service reads, by device, with its id, name, type, unit and value, filtered by `--search` as the window's search finds and by `--type`.
- R10. `config` gives the settings as thermi reads them, each overlay and item with its id.

Changing

- R11. `edit <json>` applies one edit of the settings window's own vocabulary, or a `threshold` edit setting an item's warning and critical, through the same rules as the window. A refused edit writes nothing, exits 1 and says the refusal's key and its English text.
- R12. An edit made while an overlay or the window writes the file keeps both changes, and a running overlay shows it within 1 s.
- R13. While the settings file is broken, every edit exits 4 and the file is left as it is.

MCP

- R14. `thermi-cli mcp` is an MCP server on stdio whose tools are `status`, `read`, `sensors`, `config`, `edit`, `undo` and `redo`, taking the commands' arguments and answering their JSON; a refusal or an unreachable service is a tool error saying why.
- R15. `undo` takes back the session's last edit, and `redo` makes it again, each only where what it wrote is still there; a change made since by someone else stays.
- R16. Added to Claude Code with one command from the README, an agent answers "how hot is my CPU?" with the value the overlay shows, and sets a warning that then shows on the overlay. The same works in Claude Desktop.

Sharing the PC

- R17. Between calls the MCP server holds no connection to the sensor service, and a read hangs up as soon as it has its samples, so four MCP servers running never keep an overlay from its readings.
- R18. The overlay's and the idle window's budgets of `AGENTS.md` still hold.

## Design

**The door.** `src/Thermi/Thermi.csproj` runs the SDK's `CreateAppHost` task a second time, after the build and in the publish, writing `thermi-cli.exe` for the same `thermi.dll` with the console subsystem. `Program.cs` learns which door it was started from by its process path, so `thermi-cli` with no command prints help. `scripts/install.ps1` installs it with the rest and adds `C:\Program Files\thermi` to the machine's `PATH`, which `-Uninstall` takes back; spec 004's MSI does the same.

**Commands** join `src/Thermi/Commands/ThermiCommands.cs`. What they decide lives in Core, in a new `Agents` folder: the answers' records and their JSON (a source-generated context with stable camel-case names and kebab-case choices, like the bridge's), the reads from snapshots and settings (on `SensorMap`, `SensorCatalog` and `OverlayLayout`'s level), the samples' minimum, maximum and average, and the parse of an edit. The Windows side, in an `Agents` folder of `src/Thermi`, connects to the pipe through `SensorClient` for one read, checks the overlay's mutex for `status`, and writes through `ConfigStore`.

**Edits.** `src/Thermi/Ui/Web/PageEdits.cs` moves to Core's `Editing` folder, unchanged, and the window calls it there. An agent's edit is a `PageEdit` as the bridge's JSON writes it, or `{"kind": "threshold", "item", "warning", "critical"}`; the chart's line edits and saved looks stay the window's. Applied with `ConfigEdits.Apply` over the file read under its mutex, then written by `ConfigStore.WriteAsync`, it keeps whatever else is in the file.

**MCP.** `McpServer`, in the app's `Agents` folder, hosts the tools with `ModelContextProtocol.Core` on stdio, each tool a call of the same command code, with an `UndoHistory` for the session. The package goes into `Directory.Packages.props` and the notices.

**Tests.** The reads, samples, levels and edits are tested in Core from the recordings, the zero-means-missing and no-driver ones included. The commands run in-process on a recorded feed, as the window's tests do, since only a pipe owned by SYSTEM is trusted and no command takes another. The door runs the built `thermi-cli` from each shell under `DesktopProcess`, with commands that need no pipe, its settings under a profile of the test's own. The MCP tools are tested through the SDK's client over in-memory streams to the same server code, and once over stdio to the built `thermi-cli`.

**Rejected:** reading through the overlay, which already holds a minute of history per item. The agent would get nothing while no overlay runs, which R3 rules out, and the overlay would have to answer requests, a second protocol to secure.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
