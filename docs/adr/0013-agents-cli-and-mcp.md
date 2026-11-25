# 0013. Agents and other programs read thermi through a console door, MCP and thin SDKs

**Status**: Proposed

## Context

thermi's first users are developers whose AI agents load their PCs for hours. ADR 0001 puts the agents' CLI and MCP server in `Thermi`, as the user, and leaves open whether they need a console executable. They do: `thermi.exe` is a windowed program, so PowerShell and cmd do not wait for it, and what it prints lands after the prompt, which is why `preview` needs `Start-Process -Wait`. An agent needs a command that waits, prints JSON and exits with a code. It must work with no overlay running ("headless"): what an agent asks is answered from the sensor service and the settings file, never from the overlay.

Other programs want the readings too: a terminal's status bar, an orchestrator that slows its agents when the PC runs hot, a desktop widget. HWiNFO grew such an ecosystem on one stable shared-memory contract. thermi's equivalent needs a contract that stays stable while thermi's own pipe keeps changing (ADR 0012, spec 004's version 2), readable without admin rights, a driver or LibreHardwareMonitor, and an install that is the sensors alone.

Prior art: `hwinfo-mcp` (github.com/templarfelix/hwinfo-mcp, MIT, Go) is a read-only MCP server on stdio over HWiNFO's shared memory. It does well what thermi takes: a status tool whose every failure says its cause and its fix for the agent, sampling over a few seconds with minimum, maximum and average, and judgement left to the agent's skills, a thermal check and a checkup. It needs HWiNFO running with shared memory on, which the free edition, for personal use only, turns off after 12 hours. thermi differs in being built in and kept with the app, MIT end to end, installable as the sensors alone, judging levels by the thresholds the person set, and able to configure the overlay through Core's undoable edits.

## Decision

- **One program, two doors.** `Thermi.csproj` writes a second apphost, `thermi-cli.exe`, beside `thermi.exe`, with the SDK's own `CreateAppHost` task: the same `thermi.dll`, the console subsystem. Both doors run the same `Program.cs` and parse the same commands; `thermi-cli` with no command prints its help instead of starting the overlay. `thermi.exe` stays windowed, since a console program started at sign-in flashes a console. The install adds thermi's folder under Program Files to the machine's `PATH`.
- **The contract is `thermi-cli`'s JSON, not the pipe.** Commands print one JSON document on stdout, English, with `"schema": 1`: `status`, `read [ids] [--seconds n]`, `sensors`, `config`, `edit <json>`; and `watch [ids]` prints one such document a line, once a second, until its stdin closes or it is ended. Exit codes: 0 done, 1 refused by Core's rules, 2 bad arguments, 3 the sensor service unreachable, stopped or stale, 4 the settings file broken. Within a schema, fields are only added; a reader ignores those it does not know. Removing, renaming or changing the meaning of one is a new schema, and `--schema n` keeps answering the one before for two years. The pipe stays thermi's own, free to change between releases, and its owner check stays in one place. The contract is written down in the repo, with a JSON Schema per document that the tests check every answer against.
- **Thin SDKs read the contract only.** Each starts `thermi-cli` (found on `PATH`, then under Program Files), reads its JSON and gives typed values; no driver, no LibreHardwareMonitor, no native code, no dependency beyond its platform's standard library, MIT like thermi. When `thermi-cli` is missing, or `status` says the service is not installed, it says so with how to install thermi, and never installs anything. .NET (NuGet) ships with this spec; Node (npm) and Python (PyPI) follow, on the same contract.
- **A sensors-only install.** The installer has two features: the sensors (the `thermi-sensors` and `thermi-elevated` services, `thermi-cli`) and the overlay on top (the overlay, the window and the start at sign-in). Without the overlay there is no window to offer the driver, so a sensors-only setup installs it, when missing, through the elevated mode of ADR 0010, its one consent the installer's; the full install keeps offering it in the window. Either install updates itself as spec 004 says, since the updater lives in the service, and an update keeps the features installed.
- **Edits are Core's.** `edit` takes the page's own edit vocabulary (`PageEdit`, as JSON) plus a `threshold` edit, which is `ItemEdits.SetThreshold`. `PageEdits` moves from `src/Thermi/Ui/Web` to Core, where the window and the agents share it. Each edit is applied through `ConfigEdits`' gate and written by `ConfigStore` under the file's mutex, a replay over the file as it is then; a broken file refuses every edit, as in the window. Nothing an agent sends is a path, a URL or a file to load. Edits are not part of the SDKs' contract.
- **MCP** is `thermi-cli mcp`: a server on stdio, through the official C# SDK, `ModelContextProtocol.Core`, which keeps up with the protocol's versions (2026-07-28 dropped the handshake that 2025-11-25 clients still send). Its tools are the commands, with the same JSON, plus `undo` and `redo` of the session's own edits through Core's `UndoHistory`, which never reverts what someone else changed since. Tools only.
- **Connections.** A read connects to the pipe, takes its samples and hangs up; the MCP server holds none between calls; `watch` holds one while it runs. A Windows session's share of the pipe goes from 4 to 8 of its 32 connections, so an overlay, its window and a few watchers fit.
- **Never elevated, never prompts.** Nothing `thermi-cli` runs starts the service, installs the driver or asks for UAC; `status` says what is wrong, and a person fixes it from thermi's menu or the installer.

## Rejected

- **The pipe as the public contract**, read by each SDK: lighter than a process per client, but every SDK would carry the owner check (native calls in Node and Python), and every pipe change of spec 004 would break other people's programs. If a client needs it lighter, a .NET reader of the pipe can come later, behind the same contract.
- **Shared memory, as HWiNFO**: a section any user can map is one any program can forge, unless the owner is checked, which again needs native calls in every SDK.
- **A local HTTP endpoint**: a port any process, and any web page through the browser, can reach.
- **A separate console project** referencing `Thermi`: a second assembly and composition root to keep in step. **`thermi.com`**, as `devenv.com`: Git Bash, where many agents run, picks `thermi.exe`. **A home-made MCP server**: two protocol generations, and they keep moving.

## Consequences

- An agent's command waits and exits like any other in every shell, `preview` and `cpu-load` included.
- A client pays a `thermi-cli` process, about 30 MB while it watches. Status bars that refresh by running `read` pay its start each time instead.
- `thermi.dll` references the MCP SDK; it loads only in `mcp`, so the overlay's and the window's budgets do not move.
- Minimum, maximum and average are of the samples a call took, up to 60 s: a headless call has no other history.
- One user's programs can hold 8 of the pipe's connections, so four users fill it.
- An agent's edit shows on a running overlay within its reload, but is not in the window's undo.

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md`: "Whether the CLI and MCP need their own small console executable is decided when they are built" becomes: they run in `thermi-cli.exe`, a console apphost of the same `thermi.dll`.
- `docs/adr/0003-sensor-service.md`: "a Windows session holds at most 4 of its 32 connections" becomes 8.
