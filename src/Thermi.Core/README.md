# Thermi.Core

Everything thermi decides without Windows. Which sensor is the CPU temperature, how a value reads, what the overlay shows and where, whether an edit is allowed and how it is undone, what an agent gets back: all of it lives here, as plain data, so it is tested from recordings of real PCs without any hardware.

It depends on nothing, Windows included: its target framework is `net10.0`, and `RepoRulesTests` keeps it that way. Code that needs a Windows API goes in `src/Thermi.Windows` or `src/Thermi.Sensors.Windows`. It keeps 90 % line and branch coverage (`scripts/check.ps1`) and a mutation score of 78 % (`scripts/mutation.ps1`), and takes time only from an injected `TimeProvider`.

| Folder | What lives there |
| --- | --- |
| `Sensors` | Snapshots, metrics, the sensor map with its "zero means not read" rule, the window's catalog and history, and the fan finder. |
| `Settings` | The settings file: its records, JSON, migration from older versions, rules and thresholds. |
| `Editing` | Every edit, the gate that refuses a bad one with a `Refusal`, and undo. |
| `Look` | What the overlay draws: layout, look, formatting, saved styles, item history, charts. |
| `Painting` | What every OS's painter draws alike: the sizes, the pills' parts, the meters' and graphs' shapes, and where each item was drawn. |
| `Placement` | Where an overlay goes: snaps, the taskbar row, desktop gestures, monitor ids, OLED care, visibility. |
| `Search` | The window's one search. |
| `Frames` | Presents counted into frame rates, and the game in front. |
| `Pipe` | The sensor pipe's lines: the catalog once, then the values; and the feed a host reads them through. |
| `Bridge` | The interface the settings page calls, and its wire. |
| `Agents` | The answers of `thermi-cli`, the agents' contract; what the person lets agents do; and the window's AI agents section, how to connect one and what they changed. |
| `Tray` | What the tray icons and the menus show, as data. |
| `Translations` | Every text a user reads, English and Spanish. |
| `Install` | The install folder's layout, the driver's version, the start at sign-in, the user's own sensor host, installing for everyone. |
| `Updates` | Manifests, versions, the schedule, and when a switch passes or rolls back. |
| `Messages` | The messages between the overlay and the window. |

Entry points worth knowing:

- `Sensors/SensorMap.cs`: which sensor feeds each metric.
- `Look/OverlayLayout.cs`: what the overlay shows, item by item.
- `Editing/ConfigEdits.cs`: the gate every edit passes, from the window, the overlay or an agent.
- `Editing/UndoHistory.cs`: undo, by item id, skipping what someone else changed since. A new field of the settings goes into `Editing/ConfigFields.cs`, or undo cannot take it back.
- `Settings/OverlayConfig.cs`: the settings file's fields and defaults.
