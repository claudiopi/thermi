# thermi for AI agents and other programs

`thermi-cli` reads the PC's sensors and changes thermi's settings, from any shell, with or without an overlay on screen. It is installed with thermi, or with its sensors alone (`scripts/install.ps1 -SensorsOnly`), and is on the machine's `PATH`. It never runs elevated, never asks for administrator rights and never installs or starts anything: when the sensors cannot be read, `status` says why and what a person does about it. Why it is built this way: `docs/adr/0013-agents-cli-and-mcp.md`.

## As an MCP server

```
claude mcp add thermi -- thermi-cli mcp
```

In Claude Desktop, the same under `mcpServers` in its config: `"thermi": { "command": "thermi-cli", "args": ["mcp"] }`.

The tools are the commands below but `watch`, `find-cpu-fan` and `start-with-windows` as `find_cpu_fan` and `start_with_windows`, with `undo` and `redo` of the session's own edits. `status`, `read`, `sensors`, `config`, `monitors` and `looks` are read-only; `edit`, `look` and `start_with_windows` are marked destructive, since deleting an overlay, resetting the settings, deleting a look's file or the start at sign-in are beyond undo. A refusal or a service that cannot be read is a tool error that says why. Changes made at once run one at a time, in no set order; a client that waits for each answer gets its own order. Between calls the server holds no connection to the sensors.

## Commands

| Command | What it answers |
| --- | --- |
| `status` | Whether the sensor service runs and its data is fresh, whether the driver looks missing, whether an overlay runs, why the settings file cannot be used; each problem with its cause and fix. |
| `read [ids] [--seconds n]` | Metrics (`cpu.temperature`), sensor ids and items (`item:<id>`), every metric and item when none but network addresses, which say where the PC is, so only an id asks for one. With n, from 1 to 60, sampled once a second for n seconds; from 2 on, each value has its minimum, maximum and average. |
| `watch [ids]` | `read`'s document, one line a second, until its stdin closes, or until the service goes quiet, which ends it with exit 3. |
| `sensors [--search text] [--type t]` | Every sensor the service reads, by device, found as the settings window's search finds; an address's text only with `--type address`. |
| `config` | The settings as thermi reads them. |
| `monitors` | The monitors, their bounds and taskbars, in Windows' pixels. |
| `looks`, `look wear/save/rename/delete/export/import` | The saved looks, with the settings window's rules; a look file goes in and out as text. |
| `edit <json>` | One change of the settings, by the settings window's rules. |
| `find-cpu-fan [--use]` | Loads every core for up to 30 s, at idle priority, and says which fan answered; it runs one at a time with the other changes, and a run starts at most once in 2 minutes, from any process, a run sooner or at once refused with `runsAgainIn` seconds; with `--use`, it becomes the first overlay's CPU fan, or the answer says which fan it found and the refusal's key. |
| `start-with-windows [on\|off]` | Says or sets whether the overlay starts at sign-in. |

An overlay is counted from 0 here, as in `config`, where `thermi settings --overlay` counts from 1. In Git Bash, which rewrites an argument that starts with `/` into a Windows path, run a command that names a sensor id as `MSYS_NO_PATHCONV=1 thermi-cli read /lpc/...`; PowerShell, cmd and MCP pass ids as they are.

An edit is a JSON object with its `kind`, in any order: the settings window's own (`thermi`, `overlay`, `style` and `item` set one field, `addItem`, `removeItem`, `moveItem`, `newOverlay`, `duplicateOverlay`, `mergeOverlay`, `deleteOverlay`, `nudgeOverlay`, `reset`, `resetLines`, `sensorName`), or one of two the window makes by a gesture: `{"kind": "threshold", "item", "warning", "critical"}` sets an item's lines at once, null taking one out and one left out staying as it is, and `{"kind": "place", "overlay", "x", "y"}` leaves an overlay where a drag let go at that point would. Their fields are in `src/Thermi.Core/Bridge/BridgeTypes.cs`. An edit made while an overlay or the window writes the file keeps both changes. Each change an agent makes goes to `%LOCALAPPDATA%\thermi\thermi-agents.log`, and the settings as they were before its last write stay beside them, in `config.before-agent.json`.

## The contract

A command that ends with 0 or 1 prints one JSON document on stdout, with `"schema": 1`, and nothing else; one that ends with 2, 3 or 4 prints nothing there but what it had printed before, as the lines of a `watch` the service stopped answering, and `config`, which still shows the settings it could read. Errors, help and `--version` go to stderr. The documents of `status`, `read` and `sensors`, and each line of `watch`, are the contract other programs build on, with a JSON Schema each in `schema/`, written in the build from the records that answer them, so the two cannot differ.

| Exit | Means |
| --- | --- |
| 0 | Done. |
| 1 | Refused by thermi's rules: nothing was written; the document on stdout holds the refusal's key, and stderr its English text. |
| 2 | Bad arguments. |
| 3 | The sensor service is not installed, stopped, stale or held by another program. |
| 4 | The settings file is broken, newer than this thermi or locked, or Windows refused to write it or a saved look's file; nothing was written, and stderr says why. |

Each value is in its type's fixed unit, whatever the overlay shows it in: °C, % (load and fan duty), MHz, W, RPM, GB, B/s, `s`, `fps`, `ms` for a frame's time, `V`, and `ipv4` for an address, whose `text` holds it. A value that cannot be read is null, with `why`, never 0; a 0 that is read, as a fan at rest, stays 0 and says why. An item's `shown` is its reading of this moment as the overlay writes it, in its unit and decimals; an overlay item that shows a minimum, maximum, average or smoothed value shows one over its own history, which `read --seconds` gives as `min`, `max` and `average`. A choice, as a type or a problem's code, is a word in kebab-case; a reader should take a word it does not know as unknown, not as an error.

Within a schema, fields are only added. A change that removes, renames or changes the meaning of one is a new schema, and `--schema n` keeps answering the one before for two years after the new one ships. `tests/Thermi.Tests/Contract/shipped-1` holds schema 1 as the last release shipped it, and a test fails when a field it has is gone or changed.

## Libraries

- .NET: `src/Thermi.Client`, the `Thermi.Client` package, MIT and of .NET alone, which starts `thermi-cli`.
- Node and Python: later, each as thin, on this contract.
