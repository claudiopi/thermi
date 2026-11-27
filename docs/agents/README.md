# thermi for AI agents and other programs

`thermi-cli` reads the PC's sensors and changes thermi's settings, from any shell, with or without an overlay on screen. It is installed with thermi, or with its sensors alone (`scripts/install.ps1 -SensorsOnly`), and is on the machine's `PATH`. It never runs elevated, never asks for administrator rights and never installs or starts anything: when the sensors cannot be read, `status` says why and what a person does about it. Why it is built this way: `docs/adr/0013-agents-cli-and-mcp.md`.

## As an MCP server

```
claude mcp add thermi -- thermi-cli mcp
```

In Claude Desktop, the same under `mcpServers` in its config: `"thermi": { "command": "thermi-cli", "args": ["mcp"] }`. thermi's Settings, under AI agents, shows both with a copy button, naming `thermi-cli` by its full path where its folder is not on the `PATH`, and lists the last changes agents made.

## What the person lets agents do

Settings, AI agents, has a switch, "Let agents change settings", on by default and kept in the settings file as `"agents": "read"` when off. Off, every command and tool that writes, `edit`, `look` but `export`, `start-with-windows on` or `off`, `find-cpu-fan`, `undo` and `redo`, exits 1 with the refusal `Refusal.AgentsReadOnly` and writes nothing; everything that reads answers as before. An agent may turn agents' changes off with `{"kind": "thermi", "field": "agents", "value": "read"}`, never on: only the person does, in the window. The switch governs what agents ask `thermi-cli` and its MCP server for, not an agent with a shell or file tools of its own, which can write the settings file as the person can: it is no sandbox.

The tools are the commands below, with `find_cpu_fan` and `start_with_windows` spelled with underscores, no `watch`, and `undo` and `redo` of the session's own edits.

- `status`, `read`, `sensors`, `config`, `monitors`, `looks` and `preview` only read; `preview` answers its picture as an image, so the model sees the overlay it set up.
- `edit`, `look` and `start_with_windows` are marked destructive: deleting an overlay, resetting the settings, deleting a look's file or the start at sign-in are beyond undo.
- A refusal, or a service that cannot be read, is a tool error that says why.
- Changes made at once run one at a time, in no set order. Between calls the server holds no connection to the sensors.

## Commands

| Command | What it answers |
| --- | --- |
| `status` | Whether the sensor service runs and its data is fresh, whether the driver looks missing, whether an overlay runs, why the settings file cannot be used; each problem with its cause and fix. |
| `read [ids] [--seconds n]` | Metrics (`cpu.temperature`), sensor ids and items (`item:<id>`), every metric and item when none but network addresses, which say where the PC is, so only an id asks for one. With n, from 1 to 60, sampled once a second for n seconds; from 2 on, each value has its minimum, maximum and average. |
| `watch [ids]` | `read`'s document, one line a second, until its stdin closes, or until the service goes quiet, which ends it with exit 3. |
| `sensors [--search text] [--type t]` | Every sensor the service reads, by device, found as the settings window's search finds; an address's text only with `--type address`. |
| `config` | The settings as thermi reads them. |
| `monitors` | The monitors, their bounds and taskbars, in Windows' pixels. |
| `preview [--overlay n] [--scale s]` | An overlay, the first when none, painted as it draws the readings of now on the taskbar's color, or high contrast's, at its size times s, 0.25 to 8, or the largest that keeps it within 2000 px a side, which may be under 0.25: the PNG in base64 as `png`, with its `width` and `height` in pixels and the `scale` used. One it could not paint ends with 1 and nothing on stdout; `preview <snapshot> <png>` writes a saved snapshot's picture to its file and ends with 0, with nothing on stdout. It is one reading: a graph shows one second, a minimum, maximum, average or smoothed value the value of now, an item naming the busiest process none, and an item shown only on alert only if it alerts now. |
| `looks`, `look wear/save/rename/delete/export/import` | The saved looks, with the settings window's rules; a look file goes in and out as text. |
| `edit <json>` | One change of the settings, by the settings window's rules. |
| `find-cpu-fan [--use]` | Loads every core for up to 30 s, at idle priority, and says which fan answered; it runs one at a time with the other changes, and a run starts at most once in 2 minutes, from any process, a run sooner or at once refused with `runsAgainIn` seconds; with `--use`, it becomes the first overlay's CPU fan, or the answer says which fan it found and the refusal's key. |
| `start-with-windows [on\|off]` | Says or sets whether the overlay starts at sign-in. |

An overlay is counted from 0 here, as in `config`, where `thermi settings --overlay` counts from 1. In Git Bash, which rewrites an argument that starts with `/` into a Windows path, run a command that names a sensor id as `MSYS_NO_PATHCONV=1 thermi-cli read /lpc/...`; PowerShell, cmd and MCP pass ids as they are.

An edit is a JSON object with its `kind`; its fields are in `src/Thermi.Core/Bridge/BridgeTypes.cs`, each one given, and null only where its type takes null: an edit short of one is a bad argument.

| Kind | Does |
| --- | --- |
| `thermi`, `overlay`, `style`, `item` | Set one field, as the settings window does. |
| `addItem`, `removeItem`, `moveItem`, `sensorName` | Change an overlay's items. |
| `movePill`, `dropPill`, `dropItem` | Move a pill among its overlay's pills by `by`, or a pill or an item before another's place, in any overlay, as the window's list does. |
| `newOverlay`, `duplicateOverlay`, `mergeOverlay`, `deleteOverlay`, `nudgeOverlay` | Change the overlays. |
| `reset`, `resetLines` | Reset the settings, or an item's lines. |
| `threshold` | `{"kind": "threshold", "item", "warning", "critical"}` sets an item's lines at once; null takes one out, and one left out stays. |
| `place` | `{"kind": "place", "overlay", "x", "y"}` leaves an overlay where a drag let go at that point would. |

An edit made while the overlay or the window writes the file keeps both changes. Each change an agent makes goes to `%LOCALAPPDATA%\thermi\thermi-agents.log`, and the settings as they were before its last change stay beside them, in `config.before-agent.json`.

## The contract

A command that ends with 0 or 1 prints one JSON document on stdout, with `"schema": 1`, and nothing else, but for three that print nothing there: a write from an elevated terminal and a `preview` the OS could not paint, which end with 1, and `preview` of a saved snapshot, which writes its picture to its file and ends with 0. One that ends with 2, 3 or 4 prints nothing there but what it had printed before, as the lines of a `watch` the service stopped answering, and `config`, which still shows the settings it could read. Errors, help and `--version` go to stderr. The documents of `status`, `read`, `sensors` and `preview`, and each line of `watch`, are the contract other programs build on, with a JSON Schema each in `schema/`, written in the build from the records that answer them, so the two cannot differ.

| Exit | Means |
| --- | --- |
| 0 | Done. |
| 1 | Refused by thermi's rules, or by the person, who lets agents only read: nothing was written; the document on stdout holds the refusal's key, and stderr its English text. A command that writes, run from an elevated terminal, is refused with nothing on stdout, as a `preview` the OS could not paint is. |
| 2 | Bad arguments. |
| 3 | The sensor service is not installed, stopped, stale or held by another program, or a per-user install's thermi is not running. |
| 4 | The settings file is broken, newer than this thermi or locked, or Windows refused to write it or a saved look's file; nothing was written, and stderr says why. |

Each value is in its type's fixed unit, whatever the overlay shows it in: °C, % (load and fan duty), MHz, W, RPM, GB, B/s, `s`, `fps`, `ms` for a frame's time, `V`, and `ipv4` for an address, whose `text` holds it. A value that cannot be read is null, with `why`, never 0; a 0 that is read, as a fan at rest, stays 0 and says why. An item's `shown` is its reading of this moment as the overlay writes it, in its unit and decimals; an overlay item that shows a minimum, maximum, average or smoothed value shows one over its own history, which `read --seconds` gives as `min`, `max` and `average`. A choice, as a type or a problem's code, is a word in kebab-case; a reader should take a word it does not know as unknown, not as an error.

Within a schema, fields are only added. A change that removes, renames or changes the meaning of one is a new schema, and `--schema n` keeps answering the one before for two years after the new one ships. `tests/Thermi.Tests/Contract/shipped-1` holds schema 1 as the last release shipped it, and a test fails when a field it has is gone or changed.

## Libraries

- .NET: `src/Thermi.Client`, the `Thermi.Client` package, MIT and of .NET alone, which starts `thermi-cli`.
- Node and Python: later, each as thin, on this contract.
