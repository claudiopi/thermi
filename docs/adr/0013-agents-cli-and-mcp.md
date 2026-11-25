# 0013. Agents read and configure thermi through a console door and MCP

**Status**: Proposed

## Context

thermi's first users are developers whose AI agents load their PCs for hours. ADR 0001 puts the agents' CLI and MCP server in `Thermi`, as the user, and leaves open whether they need a console executable. They do: `thermi.exe` is a windowed program, so PowerShell and cmd do not wait for it, and what it prints lands after the prompt, which is why `preview` needs `Start-Process -Wait`. An agent needs a command that waits, prints JSON and exits with a code. It must work with no overlay running ("headless"): what an agent asks is answered from the sensor service and the settings file, never from the overlay.

## Decision

- **One program, two doors.** `Thermi.csproj` writes a second apphost, `thermi-cli.exe`, beside `thermi.exe`, with the SDK's own `CreateAppHost` task: the same `thermi.dll`, the console subsystem. Both doors run the same `Program.cs` and parse the same commands; `thermi-cli` with no command prints its help instead of starting the overlay. `thermi.exe` stays windowed, since a console program started at sign-in flashes a console. The install adds thermi's folder under Program Files to the machine's `PATH`.
- **Commands**, each printing one JSON document on stdout, English and stable, with `"schema": 1`: `status` (the service, the driver's likely absence, whether an overlay runs, the settings file's problem), `read [ids] [--seconds n]` (metrics, sensors or items, now or as the minimum, maximum and average of n seconds of samples, with each item's level), `sensors [--search text] [--type t]` (the catalog), `config` (the settings as Core reads them), `edit <json>` (one edit). Exit codes: 0 done, 1 refused by Core's rules, 2 bad arguments, 3 the sensor service unreachable, stopped or stale, 4 the settings file broken. Help and errors go to stderr.
- **Edits are Core's.** `edit` takes the page's own edit vocabulary (`PageEdit`, as JSON) plus a `threshold` edit, which is `ItemEdits.SetThreshold`. `PageEdits` moves from `src/Thermi/Ui/Web` to Core, where the window and the agents share it. Each edit is applied through `ConfigEdits`' gate and written by `ConfigStore` under the file's mutex, a replay over the file as it is then; a broken file refuses every edit, as in the window. A running overlay and window see the change as any outside change. Nothing an agent sends is a path, a URL or a file to load.
- **MCP** is `thermi-cli mcp`: a server on stdio, through the official C# SDK, `ModelContextProtocol.Core`, which keeps up with the protocol's versions (2026-07-28 dropped the handshake that 2025-11-25 clients still send). Its tools are the commands above, with the same JSON, plus `undo` and `redo` of the session's own edits through Core's `UndoHistory`, which never reverts what someone else changed since. Tools only: no resources, prompts or sampling.
- **Short connections.** A read connects to the pipe, takes what it needs and hangs up; the MCP server holds no connection between calls. ADR 0003 gives a Windows session 4 of the pipe's connections, and a developer may run several agents, each with its own server: held connections would lock out the overlay.
- **Never elevated, never prompts.** Nothing an agent runs starts the service, installs the driver or asks for UAC; `status` says what is wrong, and a person fixes it from thermi's menu.

## Rejected

- **A separate console project** referencing `Thermi`: a second assembly and composition root to keep in step, for what a second apphost of the same one gives.
- **`thermi.com`** beside `thermi.exe`, as Visual Studio's `devenv.com`: cmd and PowerShell pick it first, but Git Bash, where many agents run commands, picks `thermi.exe`.
- **`thermi.exe` as a console program** that frees its console: the console flashes at every sign-in, and Windows' manifest option to start detached is too new for thermi's oldest Windows.
- **A home-made MCP server in Core**: tools over JSON-RPC are small, but two protocol generations, with and without a handshake, are not, and they keep moving.

## Consequences

- An agent's command waits and exits like any other in every shell, `preview` and `cpu-load` included.
- `thermi.dll` references the MCP SDK and its few dependencies; they load only in `mcp`, so the overlay's and the window's budgets do not move. `scripts/measure.ps1` checks it, `THIRD-PARTY-NOTICES.md` lists them.
- Minimum, maximum and average are of the samples a call took, up to 60 s, not of the overlay's history: a headless call has no other.
- An agent's edit shows on a running overlay within its reload, and in an open window, but is not in the window's undo.

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md`: "Whether the CLI and MCP need their own small console executable is decided when they are built" becomes: they run in `thermi-cli.exe`, a console apphost of the same `thermi.dll`.
