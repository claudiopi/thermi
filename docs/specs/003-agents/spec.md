# 003. AI agents and other programs read the sensors and configure thermi

**Status**: done

**Budget**: 54 .NET and 1 page cases

thermi exists because AI agents running for hours cooked their owner's PC, so an agent can ask how hot the PC is, back off when it runs hot, and set thermi up for the person. `thermi-cli` gives it the sensors and the settings from a command line or as an MCP server, with or without an overlay on screen, and the person decides in Settings whether agents may change anything. Other programs, a terminal's status bar, an orchestrator that throttles its agents, a desktop widget, read the same sensors through a contract that stays stable, also with the sensors installed alone. The decisions are in `docs/adr/0013-agents-cli-and-mcp.md`.

## Requirements

### The door

- R1. `thermi-cli` runs from PowerShell, cmd and Git Bash once thermi is installed: the shell waits for it, it prints to that shell, and it exits with its code. With no command it prints its help; it never starts an overlay.
- R2. A command that ends with 0 or 1 prints one JSON document on stdout, with `"schema": 1`, and nothing else; one that ends with 2, 3 or 4 prints only what it had printed before, as a watch's lines, or `config`'s settings as read. Errors and help go to stderr. Exit codes: 0 done, 1 refused by thermi's rules, 2 bad arguments, 3 the sensor service unreachable, stopped or stale, 4 the settings file broken, newer or locked, or a write of it or of a saved look's file refused by Windows.
- R3. Every command works the same with no overlay and no settings window running.
- R4. Nothing `thermi-cli` does runs elevated, asks for administrator rights, starts the sensor service or installs the driver.
  Note, 2026-11-28: on Linux, systemd starts the sensor service when any client connects to its socket, `thermi-cli` too, as spec 006's R5 asks; R15 there states it as an exception to this one.

### Reading

- R5. `status` says whether the sensor service runs and its data is fresh, whether the driver looks missing, whether an overlay runs, and why the settings file cannot be used, if it cannot; each problem says, for an agent, its cause and what a person does to fix it.
- R6. `read` with no ids gives every metric and every item of every overlay but network addresses, which only an id asks for; with ids, those alone, each a metric (`cpu.temperature`), a sensor id or an item id. A metric reads as the first overlay maps it, the CPU fan it was given included. Each value has its unit, and one that cannot be read is null with why, never 0.
- R7. `read --seconds n`, n from 1 to 60, samples once a second for n seconds and gives each value's minimum, maximum, average and number of samples, beside the last.
- R8. Each item read gives its level as the overlay judges it: normal, warning or critical, with the thresholds that decide it.
- R9. `sensors` lists every sensor the service reads, by device, with its id, name, type, unit and value, filtered by `--search` as the window's search finds and by `--type`; an address's text comes only with `--type address`.
- R10. `config` gives the settings as thermi reads them, each overlay and item with its id.

### Changing

- R11. `edit <json>` applies one edit of the settings window's own vocabulary, or a `threshold` edit setting an item's warning and critical, through the same rules as the window. A refused edit writes nothing, exits 1 and says the refusal's key and its English text.
- R12. An edit made while an overlay or the window writes the file keeps both changes, and a running overlay shows it within 1 s; each change an agent makes is logged, and the settings as they were before its last write are kept beside them.
- R13. While the settings file is broken, every edit exits 4 and the file is left as it is.
- R26. `looks` lists the saved looks; `look` wears one on an overlay or on all, saves an overlay's look under a name, renames or deletes a saved one, gives an overlay's or a saved look's file as text, and wears a look from such text; each with the window's rules and refusals, and no path to read or write.
- R27. `monitors` lists the monitors with their ids, bounds and taskbars, in Windows' pixels, and a `place` edit leaves an overlay where a drag let go at that point would, off its snap.
- R28. `find-cpu-fan` loads the CPU as the window's finder does, at idle priority, for no longer, one at a time with the other changes and at most once in 2 minutes, and says which fan answered, or why none did; with `--use`, that fan becomes the first overlay's CPU fan, one edit, or the answer still names it with the edit's refusal.
- R29. `start-with-windows [on|off]` says or sets whether the overlay starts at sign-in, as the window's switch does; on a sensors-only install it says there is no overlay to start.

### The person's say, in the settings window

- R30. Settings has an "AI agents" section, for all of thermi rather than one overlay, beside About. One line says what agents do through thermi: read the sensors, and change the settings by the window's rules. Below it, the command that adds thermi to Claude Code and the JSON that adds it to a client set up by a file, as Claude Desktop, Cursor or VS Code, each with a button that copies it; both name `thermi-cli` as it is when its folder is on the `PATH`, and by its full path when it is not.
- R31. A switch "Let agents change settings", on by default, is a field of the settings file that undo takes back. Off, every command and MCP tool that writes (`edit`, `look` but `export`, `start-with-windows on` or `off`, `find-cpu-fan`, `undo` and `redo`) is refused with exit 1 and writes nothing, its refusal's key and English text saying the person turned it off in Settings, AI agents; reads still answer. An agent may turn it off, never on: an edit that turns it on is refused, on or off. A settings file that cannot be read, broken, newer or locked, says nothing of the switch, so every write exits 4 and writes nothing, as R13 has an edit do.
- R32. The section lists the last 5 changes agents made, newest first, from `thermi-agents.log`, each with its time and what it changed, with a link that opens the log; it lists nothing when no agent changed anything.

### MCP

- R14. `thermi-cli mcp` is an MCP server on stdio whose tools are `status`, `read`, `sensors`, `config`, `monitors`, `looks`, `edit`, `look`, `find_cpu_fan`, `start_with_windows`, `undo` and `redo`, taking the commands' arguments and answering their JSON; a refusal or an unreachable service is a tool error saying why. `status`, `read`, `sensors`, `config`, `monitors` and `looks` are marked read-only, and `edit`, `look` and `start_with_windows` destructive.
- R15. `undo` takes back the session's last edit, and `redo` makes it again, each only where what it wrote is still there; a change made since by someone else stays.
- R16. Added to Claude Code with one command from the README, an agent answers "how hot is my CPU?" with the value the overlay shows, and sets a warning that then shows on the overlay. The same works in Claude Desktop.
  Release check: thermi-cli in Claude Code and Claude Desktop.

### Sharing the PC

- R17. Between calls the MCP server holds no connection to the sensor service, and a read hangs up as soon as it has its samples, so four MCP servers running never keep an overlay from its readings.
- R18. With agents' MCP servers and commands running, the overlay's and the idle window's budgets of `AGENTS.md` hold.
  Release check: Costs at rest.
- R19. `watch [ids]` prints `read`'s document as one line each second until its stdin closes or it is ended, holding one connection to the service while it runs; a Windows session may hold 8 of them, an overlay and its window included.

### The contract

- R20. The JSON of `status`, `read`, `sensors` and `watch` is a documented contract with a JSON Schema per document, and every answer the tests see validates against it.
- R21. Within a schema, fields are only added. A change that removes, renames or changes the meaning of one is a new schema, and `--schema n` answers the one before for two years after the new one ships.

### The SDK

- R22. A .NET package on NuGet, MIT, with no dependency beyond .NET and no native code, gives `status`, `read`, `sensors` and `watch` as typed values, by starting `thermi-cli` found on `PATH`, or where an install for everyone (`C:\Program Files\thermi`) or for one person alone (`%LOCALAPPDATA%\Programs\thermi`) puts it.
- R23. Without `thermi-cli`, or with the service not installed, the SDK fails with an error that says so and how to install thermi, and installs nothing.

### Installing the sensors alone

- R24. The installer installs the sensors alone (the sensor service, its elevated helper and `thermi-cli`) or with the overlay and window on top; without the overlay, it installs the driver during setup when it is missing, with the installer's one administrator prompt.
  Release check: A sensors-only install.
- R25. A sensors-only install updates itself as spec 004's updates do, and an update, or installing the full thermi over it, keeps what was installed and adds only what was asked.
  Release check: A sensors-only install.

## Design

| Where | What |
| --- | --- |
| `src/Thermi.Windows/Thermi.Windows.csproj` | Runs the SDK's `CreateAppHost` a second time, writing `thermi-cli.exe` for the same `thermi.dll` with the console subsystem. `Program.cs` knows the door it was started from by its process path (`ThermiCommands.IsConsoleDoor`), so `thermi-cli` with no command prints help. |
| `src/Thermi.Cli` | The commands (`Commands/AgentCommandLine.cs`) and their runs (`Agents/AgentCommands.cs`), the fan finder's run, the samples, undo and the MCP server (`Agents/McpTools.cs`, on `ModelContextProtocol.Core` over stdio, an `UndoHistory` a session). It targets plain `net10.0`: what it needs of the OS comes through `IAgentEnvironment`. |
| `src/Thermi.Windows/Agents` | `AgentEnvironment`, Windows' answers to `IAgentEnvironment`: the pipe through `SensorClient`, the overlay's mutex, the install's choice under `HKLM\SOFTWARE\thermi`, the monitors and the start at sign-in. |
| `src/Thermi.Core/Agents` | What the commands decide: the answers' records and their JSON (source-generated, camel-case names and kebab-case choices, like the bridge's), the reads from snapshots and settings, the parse of an edit, `AgentRules` and `AgentEdits` for the person's switch, `AgentSetup` and `AgentActivity` for the window's section. |
| `src/Thermi.Client` | The .NET SDK: a `net10.0` library with no package references, which starts `thermi-cli --schema 1` and reads its stdout; `watch` is an `IAsyncEnumerable`. |
| `docs/agents` | The contract, with a JSON Schema per document. |
| `src/Thermi.Installer` | The machine MSI's two features, sensors and overlay, chosen by `SENSORSONLY`. |

**Edits.** An agent's edit is a `PageEdit` as the bridge's JSON writes it (Core's `PageEdits`, the same the window calls), or `{"kind": "threshold", "item", "warning", "critical"}`, or `{"kind": "place", "overlay", "x", "y"}`, which is `OverlayEdits.Drop`. It is applied with `ConfigEdits.Apply` over the file read under its mutex and written by `ConfigStore.WriteAsync`, so whatever else is in the file stays. Looks go through `SavedStyles` and `StyleEdits`, as the window's `WindowLooks` does, with text in and out in place of file dialogs and the clipboard; the finder is Core's `FanFinder` with `CpuLoad`'s load; the start at sign-in is the window's own. The window and the agents share one code for each, so neither copies the other.

**The person's say.** The switch is `agents` in the settings file, `change` by default and so left out, or `read`, with its undo field in `ConfigFields`. Every write of `AgentCommands` asks `AgentRules`, against the file as that write finds it; so do the writes that touch no settings (a look's file, the start at sign-in, the finder), against the file as read. `AgentEdits` refuses an edit that sets it to `change`. The window's section reads `Agents()` over the bridge: `AgentSetup` writes the command and the JSON from `thermi-cli`'s path and the folders on the `PATH`, and `AgentActivity` reads the changes back from `thermi-agents.log`, whose lines `AgentCommands` writes with Core's template, so the two cannot drift.

**The sensors-only install.** The choice is a value under `HKLM\SOFTWARE\thermi` that the MSI writes and `thermi-cli` reads, since an update of spec 004 is a version folder beside the last, never the MSI; the major upgrade reads it back, and `SENSORSONLY=0` installs the full thermi over it. Without the overlay there is no Start menu entry, no start at sign-in and no overlay started after the install. The setup's driver step starts `thermi-elevated` with `install-driver` as SYSTEM, as `docs/adr/0010-driver-and-service-from-the-window.md` says. `scripts/install.ps1 -SensorsOnly` does the same from a build. A session's share of the pipe is `SensorPipeServer.PerSession`.

**Tests.** The reads, samples, levels and edits are tested in Core from the recordings, the missing-value and no-driver ones included. The commands run in-process on a recorded feed (`tests/Thermi.Tests/Agents/AgentRig.cs`), since only a pipe owned by SYSTEM is trusted. The door runs the built `thermi-cli` from each shell under `DesktopProcess`. The MCP tools are tested through the SDK's client over in-memory streams, and once over stdio to the built `thermi-cli`. Every answer of the command tests validates against the schemas, and the SDK's records round-trip each Core answer of the recordings.

**Rejected:** reading through the overlay, which already holds a minute of history per item. The agent would get nothing while no overlay runs, which R3 rules out, and the overlay would have to answer requests, a second protocol to secure.

## Acceptance

The steps are in `acceptance.json`; what stays open is in `tasks.md`.
