# 003. AI agents and other programs read the sensors and configure thermi

**Status**: approved

## Why

thermi exists because AI agents running for hours cooked their owner's PC. The agent itself should be able to ask how hot the PC is, back off when it runs hot, and set thermi up for the person, from a command line or as an MCP server, with or without an overlay on screen. Other programs, a terminal's status bar, an orchestrator that throttles its agents, a desktop widget, should read the same sensors through a contract that stays stable, as HWiNFO's shared memory grew an ecosystem, with or without thermi's overlay installed. The decisions are in `docs/adr/0013-agents-cli-and-mcp.md`.

## Requirements

The door

- R1. `thermi-cli` runs from PowerShell, cmd and Git Bash once thermi is installed: the shell waits for it, it prints to that shell, and it exits with its code. With no command it prints its help; it never starts an overlay.
- R2. Every command prints one JSON document on stdout, with `"schema": 1`, and nothing else; errors and help go to stderr. Exit codes: 0 done, 1 refused by thermi's rules, 2 bad arguments, 3 the sensor service unreachable, stopped or stale, 4 the settings file broken.
- R3. Every command works the same with no overlay and no settings window running.
- R4. Nothing `thermi-cli` does runs elevated, asks for administrator rights, starts the sensor service or installs the driver.

Reading

- R5. `status` says whether the sensor service runs and its data is fresh, whether the driver looks missing, whether an overlay runs, and why the settings file cannot be used, if it cannot; each problem says, for an agent, its cause and what a person does to fix it.
- R6. `read` with no ids gives every metric and every item of every overlay; with ids, those alone, each a metric (`cpu.temperature`), a sensor id or an item id. A metric reads as the first overlay maps it, the CPU fan it was given included. Each value has its unit, and one that cannot be read is null with why, never 0.
- R7. `read --seconds n`, n from 1 to 60, samples once a second for n seconds and gives each value's minimum, maximum, average and number of samples, beside the last.
- R8. Each item read gives its level as the overlay judges it: normal, warning or critical, with the thresholds that decide it.
- R9. `sensors` lists every sensor the service reads, by device, with its id, name, type, unit and value, filtered by `--search` as the window's search finds and by `--type`.
- R10. `config` gives the settings as thermi reads them, each overlay and item with its id.

Changing

- R11. `edit <json>` applies one edit of the settings window's own vocabulary, or a `threshold` edit setting an item's warning and critical, through the same rules as the window. A refused edit writes nothing, exits 1 and says the refusal's key and its English text.
- R12. An edit made while an overlay or the window writes the file keeps both changes, and a running overlay shows it within 1 s.
- R13. While the settings file is broken, every edit exits 4 and the file is left as it is.
- R26. `looks` lists the saved looks; `look` wears one on an overlay or on all, saves an overlay's look under a name, renames or deletes a saved one, gives an overlay's or a saved look's file as text, and wears a look from such text; each with the window's rules and refusals, and no path to read or write.
- R27. `monitors` lists the monitors with their ids, bounds and taskbars, in Windows' pixels, and a `place` edit leaves an overlay where a drag let go at that point would, off its snap.
- R28. `find-cpu-fan` loads the CPU as the window's finder does, for no longer, and says which fan answered, or why none did; with `--use`, that fan becomes the first overlay's CPU fan, one edit.
- R29. `start-with-windows [on|off]` says or sets whether the overlay starts at sign-in, as the window's switch does; on a sensors-only install it says there is no overlay to start.

MCP

- R14. `thermi-cli mcp` is an MCP server on stdio whose tools are `status`, `read`, `sensors`, `config`, `monitors`, `looks`, `edit`, `look`, `find_cpu_fan`, `start_with_windows`, `undo` and `redo`, taking the commands' arguments and answering their JSON; a refusal or an unreachable service is a tool error saying why. `status`, `read`, `sensors`, `config`, `monitors` and `looks` are marked read-only.
- R15. `undo` takes back the session's last edit, and `redo` makes it again, each only where what it wrote is still there; a change made since by someone else stays.
- R16. Added to Claude Code with one command from the README, an agent answers "how hot is my CPU?" with the value the overlay shows, and sets a warning that then shows on the overlay. The same works in Claude Desktop.

Sharing the PC

- R17. Between calls the MCP server holds no connection to the sensor service, and a read hangs up as soon as it has its samples, so four MCP servers running never keep an overlay from its readings.
- R18. The overlay's and the idle window's budgets of `AGENTS.md` still hold.
- R19. `watch [ids]` prints `read`'s document as one line each second until its stdin closes or it is ended, holding one connection to the service while it runs; a Windows session may hold 8 of them, an overlay and its window included.

The contract

- R20. The JSON of `status`, `read`, `sensors` and `watch` is a documented contract with a JSON Schema per document, and every answer the tests see validates against it.
- R21. Within a schema, fields are only added. A change that removes, renames or changes the meaning of one is a new schema, and `--schema n` answers the one before for two years after the new one ships.

The SDKs

- R22. A .NET package on NuGet, MIT, with no dependency beyond .NET and no native code, gives `status`, `read`, `sensors` and `watch` as typed values, by starting `thermi-cli` found on `PATH` or under Program Files.
- R23. Without `thermi-cli`, or with the service not installed, the SDK fails with an error that says so and how to install thermi, and installs nothing.

Installing the sensors alone

- R24. The installer installs the sensors alone (the sensor service, its elevated helper and `thermi-cli`) or with the overlay and window on top; without the overlay, it installs the driver during setup when it is missing, with the installer's one administrator prompt.
- R25. A sensors-only install updates itself as spec 004's updates do, and an update, or installing the full thermi over it, keeps what was installed and adds only what was asked.

Later, each its own spec on this contract: a Node package on npm and a Python package on PyPI, as thin as the .NET one.

## Design

**The door.** `src/Thermi/Thermi.csproj` runs the SDK's `CreateAppHost` task a second time, after the build and in the publish, writing `thermi-cli.exe` for the same `thermi.dll` with the console subsystem. `Program.cs` learns which door it was started from by its process path, so `thermi-cli` with no command prints help. `scripts/install.ps1` installs it with the rest and adds `C:\Program Files\thermi` to the machine's `PATH`, which `-Uninstall` takes back; spec 004's MSI does the same.

**Commands** join `src/Thermi/Commands/ThermiCommands.cs`. What they decide lives in Core, in a new `Agents` folder: the answers' records and their JSON (a source-generated context with stable camel-case names and kebab-case choices, like the bridge's), the reads from snapshots and settings (on `SensorMap`, `SensorCatalog` and `OverlayLayout`'s level), the samples' minimum, maximum and average, and the parse of an edit. The Windows side, in an `Agents` folder of `src/Thermi`, connects to the pipe through `SensorClient` for one read, checks the overlay's mutex for `status`, and writes through `ConfigStore`.

**Edits.** The window's `PageEdits`, in `src/Thermi/Ui/Web`, moves to Core's `Bridge` folder, unchanged, and the window calls it there. An agent's edit is a `PageEdit` as the bridge's JSON writes it, or `{"kind": "threshold", "item", "warning", "critical"}`; or `{"kind": "place", "overlay", "x", "y"}`, which is `OverlayEdits.Drop`; the chart's own line gestures stay the window's. The saved looks go through `SavedStyles` and `StyleEdits` as `WindowLooks` does, with text in and out in place of the window's file dialogs and clipboard; the finder is `FanFinder` fed by the same pipe reads, with the load of `CpuLoad`; the start at sign-in is `StartWithWindows`. What the window and the agents share of these is one code: Core's, or the window's `WindowFinder` and `StartWithWindows`, which the agents call, so neither copies the other. Applied with `ConfigEdits.Apply` over the file read under its mutex, then written by `ConfigStore.WriteAsync`, it keeps whatever else is in the file.

**MCP.** `McpTools`, in the app's `Agents` folder, hosts the tools with `ModelContextProtocol.Core` on stdio, each tool a call of the same command code, with an `UndoHistory` for the session. The package goes into `Directory.Packages.props` and the notices.

**Tests.** The reads, samples, levels and edits are tested in Core from the recordings, the zero-means-missing and no-driver ones included. The commands run in-process on a recorded feed, as the window's tests do, since only a pipe owned by SYSTEM is trusted and no command takes another. The door runs the built `thermi-cli` from each shell under `DesktopProcess`, with commands that need no pipe, its settings under a profile of the test's own. The MCP tools are tested through the SDK's client over in-memory streams to the same server code, and once over stdio to the built `thermi-cli`.

**The contract** is written in a new page of `docs` with its JSON Schemas beside it, and Core's answer records are their only source; a test validates every answer of the command tests against the schemas. `--schema` is a parse option that today knows only 1.

**The SDK** is a new project in `src`, a `net8.0` library with no package references, which holds the contract's records again, as the public side; a test round-trips each Core answer of the recordings through them, so the two cannot drift. It finds `thermi-cli`, runs it with `--schema 1`, and reads its stdout; `watch` is an `IAsyncEnumerable`. Packed in the CI, published by a person at a release.

**The sensors-only install** is two features of spec 004's MSI, sensors and overlay, with the sensors always on; winget offers both, and the major upgrade keeps them. An update of spec 004 is a version folder beside the last, never the MSI, so the choice is a value under `HKLM\SOFTWARE\thermi` that the MSI writes and the service reads: its launchers skip the overlay's `thermi.exe`, and the start at sign-in skips it too. Installing the full thermi over it rewrites the value and places that launcher. `thermi-cli.exe` is in each version's folder and among the launchers at the top, listed in the release's catalog and signed like the others. The setup's driver step starts `thermi-elevated` with `install-driver` after the install is in place, as SYSTEM, downloading as ADR 0010 says. Until that MSI lands, `scripts/install.ps1 -SensorsOnly` does it. The share of 8 is `SensorPipeServer.PerSession`.

**Rejected:** reading through the overlay, which already holds a minute of history per item. The agent would get nothing while no overlay runs, which R3 rules out, and the overlay would have to answer requests, a second protocol to secure.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
