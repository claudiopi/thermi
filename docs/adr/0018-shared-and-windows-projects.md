# 0018. Shared projects, and projects named for their OS

**Status**: Accepted

## Context

thermi runs only on Windows today, but its logic never needed Windows: Core targets plain `net10.0` (ADR 0001), and the settings page is a web page. A reader could not tell that from the names. The projects Thermi and Thermi.SensorHost sounded like the heart of thermi, though both are pure Windows: Win32 windows, GDI+, a Windows service, LibreHardwareMonitor. A Linux or Mac version would have had to rename them first, after v1 had made the names public.

## Decision

Projects that only make sense on Windows say so in their name. The names users see do not change: `thermi.exe`, `thermi-cli.exe`, `thermi-sensors.exe`, the services and the folders on disk stay.

| Project | Runs on | What it is |
| --- | --- | --- |
| `src/Thermi.Core` | any OS | Readings, sensor mapping, layout, config, edits and undo, the agents' answers, update decisions. |
| `src/Thermi.Settings.Web` | any OS's web view | The settings page. It talks to its host only through the bridge. |
| `src/Thermi.Settings.Types` | build time | Writes the page's TypeScript from Core. |
| `src/Thermi.Client` | any OS | The .NET SDK for agents: starts `thermi-cli` and reads its JSON. |
| `src/Thermi.Releases` | the owner's PC | Signs the update channels. Ships nowhere. |
| `src/Thermi.Windows` | Windows | `thermi.exe` and `thermi-cli.exe`: the overlay, the tray, the settings window in WebView2, the agents' door. Was Thermi. |
| `src/Thermi.Sensors.Windows` | Windows | `thermi-sensors.exe`: the sensor service and the updater. Was Thermi.SensorHost. |
| `src/Thermi.Trust` | Windows | Authenticode, catalogs and admin-only folders. |
| `src/Thermi.Installer` | Windows | The MSI. |

Namespaces follow the folders, so `Thermi.Windows.Overlay` and `Thermi.Sensors.Windows.Updates`. The app's old `Windows` folder, the thin wrappers over Win32 calls, is now `Win32`, so no namespace reads `Thermi.Windows.Windows`.

A host for another OS plugs in beside these, as two projects:

1. `Thermi.Sensors.<OS>` reads the sensors (on Linux, `hwmon`), maps nothing itself, and writes Core's snapshots with `SnapshotWriter` over that OS's local socket.
2. `Thermi.<OS>` reads those snapshots, lets Core decide what each overlay shows (`OverlayLook`, `Snaps`, `ConfigEdits`), and paints it. It hosts `Thermi.Settings.Web` in the OS's web view behind the same bridge (`IWindowBridge`, answered by `BridgeCalls`), and serves the same `thermi-cli` contract, so `Thermi.Client` and agents work unchanged.

Core keeps no Windows type, so nothing in it moves.

## Rejected

- **Keep the names until a second OS exists**: free today, but after v1 every link, script and fork names the old folders, and renaming then breaks them.
- **Rename `Thermi.Trust` to `Thermi.Trust.Windows` too**: it is Windows-only, but a second OS would check trust in a different way altogether (codesign, package signatures), not port this project. Its name says what it does, and its target framework says where.

## Consequences

- A reader sees at once what is shared and what is Windows: what ends in `.Windows` can be left alone when you work on another OS.
- Every path in the docs moved. `RepoRulesTests` fails on any doc that still cites an old path.
- What to watch: shared logic must keep landing in Core, not in `Thermi.Windows` because it is closer at hand. The review checks it; Core's target framework stops Windows types from getting in.
