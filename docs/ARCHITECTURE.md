# Architecture

How thermi is built, with the real names, so you can find any piece and change it without breaking the design. New here? Read [QUICKSTART.md](QUICKSTART.md) first. Each project's own `README.md` has its details; the reasons are in `docs/adr`.

## The system on one page

```
 Hardware (CPU, GPU, motherboard, memory, network, drives, battery)
        │ LibreHardwareMonitor, with the PawnIO driver; Windows' IP Helper for which adapters are physical
        ▼
 ┌─ thermi-sensors (src/Thermi.SensorHost) ── Windows service, LocalSystem ────┐
 │ LhmSensorSource reads ─► SensorPipeServer: the catalog once, then values    │
 └──────────────────────────────┬──────────────────────────────────────────────┘
                                ▼ named pipe "thermi-sensors": read-only, owner checked
 ┌─ thermi.exe (src/Thermi) ── the user's process, no admin rights ────────────┐
 │ SensorClient ─► LatestSnapshot ─► SensorMap ─► OverlayLayout ─► Painter      │
 │                                    (Core)    (Core, with Look)   (GDI+)      │
 │ OverlayHost: hidden "thermi-host" window · the loop, the tick, "show",      │
 │   visibility (Core), hotkeys and their status, TrayIcons ─► TrayPainter     │
 │ OverlayWindow ×1–4: layered Win32 windows · Topmost keeps them on top       │
 │   placed where Snaps (Core) says, from the monitors Desktop reads           │
 │ Settings + ConfigStore ◄─► %APPDATA%\thermi\config.json (watched, mutex)    │
 └───────────▲─────────────────────────────────────────────────────────────────┘
             │ "show", "hotkey status" to thermi-host · the settings file
 ┌─ thermi.exe settings (src/Thermi/Ui) ── a process of its own, no WPF ───────┐
 │ WindowProcess: the window's mutex, an STA thread · SettingsBeacon: hidden   │
 │   "thermi-settings", raised by a second request · WebWindow over Mica       │
 │ WebPage: WebView2 ─► the page (src/Thermi.Settings.Web, React) ─► bridge    │
 │ WindowBridge (Core's IWindowBridge) ─► Views, from SensorClient + Readings  │
 │ WindowEditor ─► Core's edits and UndoHistory ─► Settings + ConfigStore      │
 └─────────────────────────────────────────────────────────────────────────────┘
 ┌─ thermi-cli.exe (src/Thermi/Agents) ── the same thermi.dll, a console door ─┐
 │ a command or the MCP server ─► AgentCommands ─► Core's Agents: one JSON doc │
 │   reads through SystemFeed and hangs up · edits through Core + ConfigStore  │
 └─────────────────────────────────────────────────────────────────────────────┘
```

## Projects

| Project | Runs as | Depends on | Job |
| --- | --- | --- | --- |
| `src/Thermi.Core` | a library | nothing | Snapshots, metrics, sensor mapping, formatting, thresholds, config, edits, undo, layout, look and snaps, OLED care, power items and their history, saved styles, visibility, the tray's texts, hotkeys and their status, the translations, and the window's catalog, history and fan finder. |
| `src/Thermi.SensorHost` | `thermi-sensors.exe`, a service | Core, LibreHardwareMonitor, System.CommandLine, System.ServiceProcess.ServiceController | Reads the sensors and serves snapshots. |
| `src/Thermi` | `thermi.exe` and `thermi-cli.exe`, the user | Core, the Windows Desktop runtime (GDI+ only), WebView2, Serilog under Microsoft.Extensions.Logging, System.CommandLine, ModelContextProtocol.Core, and CsWin32 at build time only | The overlay, its settings file, `thermi preview`, as `thermi settings` the window that hosts the page, in a process of its own, and as `thermi-cli` the agents' commands and MCP server. |
| `src/Thermi.Settings.Web` | the page in the window's WebView2, embedded in `thermi.exe` | React, Fluent UI React v9, built by Vite | The settings window's page, which holds no logic of its own: it reads and edits through the bridge. |
| `src/Thermi.Settings.Types` | the build | Core | Writes the page's TypeScript from Core: the bridge's types and the texts' keys; and the agents' JSON Schemas. |
| `src/Thermi.Client` | a library other programs ship, `net10.0` | nothing | The .NET SDK of the agents' contract, which starts `thermi-cli`. |
| `tests/Thermi.Tests` | the test runner | Core | Behavior from recordings, file formats and the repo rules. |
| `tests/Thermi.Windows.Tests` | the test runner | all of the above | Real pipes, the service, the painter, the settings file and the window's side of the bridge. |

Why these packages and no others, and why the service takes the fewest, since it runs as LocalSystem: `docs/adr/0006-proven-libraries-for-logging-and-win32.md`. Both executables are published ReadyToRun for the PC's own processor, x64 or Arm64, by `scripts/install.ps1`.

## Names

| Name | What it is |
| --- | --- |
| **Snapshot** | Every sensor of the types thermi shows, at one moment: id, hardware, type, name, value. `Snapshot.cs`. |
| **Recording** | A snapshot saved with `thermi-sensors --dump`, replayed by tests. `tests/Thermi.Tests/Recordings`. |
| **Catalog line** | The whole snapshot, names and all, which the pipe sends each client on connecting and on any change of its sensors; every other second it sends a values line, the values alone in that order (`docs/adr/0012-sensor-pipe-catalog-once.md`). `src/Thermi.Core/Pipe/SnapshotWriter.cs`, `SnapshotLines.cs`. |
| **Metric** | A stable name for what the overlay shows, like `cpu.temperature`. `Metrics.cs`. |
| **Sensor map** | The rules from a snapshot to each metric and to each sensor an item names, and the "zero means not read" rule, for a CPU temperature, power or clock only. `SensorMap.cs`. |
| **Catalog** | Every sensor the service reads, by device, with its search, its type filter and why a sensor has no value. `SensorCatalog.cs`, which searches through `SearchIndex.cs`, the window's one search, the Sensor dropdown's filter's too; the window's view of it is `Ui/Views/WindowViews.cs`, and the page's `src/Thermi.Settings.Web/src/catalog`. |
| **Bridge** | All the page may read and ask of C#, as one interface in Core whose TypeScript the build writes for the page. `IWindowBridge.cs`, answered by `Ui/Web/WindowBridge.cs`. |
| **History** | The window's last ten minutes of each value, a second a slot, with gaps left empty. `SensorHistory.cs`. |
| **Fan finder** | Loads the CPU for a few seconds and picks the fan that answered, replayed from recorded series in tests. `FanFinder.cs`; in the window, from the catalog, `Ui/Views/WindowFinder.cs` with its load in `Commands/CpuLoad.cs`, which `thermi cpu-load` runs too. |
| **Overlay** | One of up to four in the settings, each with its items, placement, style and visibility. `OverlayConfig.cs`. |
| **Item** | One entry in an overlay: a metric, a sensor or combined sensors, with an id unique in the file, an optional other reading it warns by and threshold, and what it draws beside its value, bars, a line or nothing, its own or its overlay's (`OverlayLayout.DrawnOf`). `OverlayConfig.cs`. |
| **Edit** | One change the window makes, which gives valid settings or a `Refusal` saying why not. `ItemEdits.cs`, `OverlayEdits.cs`, and the gate in `ConfigEdits.cs`. |
| **Undo step** | What one edit or gesture changed, as operations on item ids that each remember what they found and wrote, so a hand edit is never reverted. A choice made of several fields, as what an item warns by with its threshold, alert-only and notice, is one operation. `UndoHistory.cs`, made by `ConfigDiff.cs` over `ConfigFields.cs`. |
| **Throttle** | At most one change out every 100 ms, and always the last. `Throttle.cs`. |
| **Pill** | A run of items under one label, or an item that starts one of its own, painted as one rounded shape; the style's layout can make one per item or one bar. `OverlayLayout.Groups`. |
| **Look** | A style ready to draw: sizes and weights from its preset, its font, corners and outline, the pills' colors, text colors that stay readable or Windows' high contrast ones, and where each pill goes. `OverlayLook.cs`. |
| **Style file** | A `.thermi-style` file: the look alone and its name, refused whole when broken, newer or out of range. A saved style is one in a folder, its file name made from its name. `StyleFile.cs`, `SavedStyles.cs`; the window saves, renames, deletes, exports and imports them (`Ui/Views/WindowLooks.cs`). |
| **Item history** | Each item's last minute and its running minimum, maximum and average, for stats, smoothing and lines, kept by item, one for all the overlays and one in the window, which takes the overlay's as it opens and after a reset, so the window shows each item as the overlay does. `ItemHistory.cs`. |
| **Snap** | A place on a monitor's taskbar, anywhere along it at the offset it was dropped at (`snap.offset`), or a corner of its work area, where the overlay keeps its edges, worked out at every paint from the monitors and never written as a position. Only a drag past the system's drag distance unsnaps it; a click, or any press on a locked overlay, writes nothing. `Snaps.cs`, `OverlayEdits.Release`. A snap names its monitor by the maker, model and serial of its EDID (`MonitorIds.cs`). |
| **OLED care** | The overlay moving 1 px every 3 minutes around a 1 px square inward of its snapped edges, from the time alone and never written. `OledCare.cs`. |
| **Room** | The widest value an item can show, the width kept for it. `ReadingFormatter.Widest`. |
| **Translations** | Every text a user reads, as a member of an enum marked `[Translated]`, in `Translations/Texts.resx` (English) and one file per language. Numbers follow the regional format, never the language. `Texts.cs`. |
| **Visibility** | Whether each overlay shows: not while it is hidden on its own or "Hide overlays" is set, a flag kept in memory that the hotkey toggles and showing clears, else by its own setting against a full-screen app on its monitor; a hidden overlay shows only in the window, never on the desktop. `OverlayVisibility.cs`. |
| **Hotkey status** | The overlay's answer to the window: which hotkeys it could not register, with the hash of those it tried, trusted only when that hash is of the hotkeys the window shows. `Hotkeys.cs`, `HotkeyRegistry.cs`. |
| **Tray scene** | Which tray icons show and what each shows: thermi's own icon always, with its mark's state and a tooltip of the hottest value and what is hidden, then four characters in a readable color per item that asks, each tooltip within 127 characters, an id per install. Both menus are data, and the Windows notices are state machines. `TrayScene.cs`, `TrayTexts.cs`, `ThermiMark.cs`, `Menus.cs`, `Notices.cs`; the overlay keeps them with `TrayIcons.cs` and draws them with `TrayPainter.cs`. |
| **Freshness** | A snapshot older than 3 s counts as gone, and the service as stopped without its pipe or stale with it. `LatestSnapshot` in `SnapshotLines.cs`. |
| **Host** | The overlay process's one hidden top-level window, class `thermi-host`: the message loop, the tick, the paint throttle, "show", "hotkey status", the hotkeys and the tray icons, and one layered window per overlay. `OverlayHost.cs`. |
| **Window** | The settings window, `thermi settings [--overlay <n>]`: a Win32 window hosting the page in WebView2, in a process of its own, one at a time while it holds `Local\thermi-window`, on the overlay it was asked for. `Ui/WindowProcess.cs`, `Ui/Web/WebWindow.cs`. |
| **Agent door** | `thermi-cli.exe`, a second apphost of `thermi.dll` with the console subsystem, so shells wait for it: commands that print one JSON document, or an MCP server on stdio. Each read connects to the pipe and hangs up. The answers' records, in `src/Thermi.Core/Agents`, are the contract's only source (`docs/agents/README.md`). `Agents/AgentCommands.cs`, `Agents/McpTools.cs`. |
| **Beacon** | The window process's hidden top-level window, class `thermi-settings`, which a second request finds and sends `thermi-raise` to bring the window to the front. `Ui/SettingsBeacon.cs`. |

## Rules of the road

- **Trust.** The service never reads from its pipe, and nothing of thermi's is elevated by UAC: the window's two fixes run in the `thermi-elevated` service (`docs/adr/0010-driver-and-service-from-the-window.md`). The overlay trusts a pipe only when its owner is SYSTEM or Administrators (`docs/adr/0003-sensor-service.md`).
- **Failure.** A failure in the service stops it with an error, and a sensor read stuck past 60 s ends its process; either way Windows restarts it. Nothing the overlay reads can crash it. The service's errors go to the Application event log under `thermi-sensors`; the overlay's and the window's go through an injected `ILogger` to `%LOCALAPPDATA%\thermi`, a Serilog file each, `thermi.log` and `thermi-window.log`, and `thermi-agents.log`, which the agents' commands share, rolled at 1 MB with three kept, and `--trace` adds the debug events. The elevated mode writes to the Application event log, under `thermi-sensors`.
- **Time.** Code gets time from an injected `TimeProvider`, so tests run on a fake clock.
- **Above the taskbar.** `Topmost.cs` reacts when a taskbar rises above the overlay, and the once-a-second tick looks again as a safety net, raising it only past a taskbar too (`docs/adr/0004-above-the-taskbar.md`).
- **Settings.** The file is versioned, and an older version is migrated when read (`ConfigMigration.cs`). A broken or newer file is never overwritten: the overlay keeps the last settings it read well, or the defaults if there were none, and says why (`ConfigProblem.cs`); the window changes nothing until the file is fixed or the last good settings are restored, the broken file kept beside it. Every writer holds the file's mutex from its re-read to its move into place, and a file found locked, empty, cut short or missing is tried again for 500 ms before it counts (`ConfigStore.cs`, `docs/adr/0005-settings-window-process.md`). Each overlay of the file, up to four, is a layered window of its own in the one overlay process (`OverlayHost.cs`).
- **Budget.** At rest the overlay, the idle window and the service each stay under a CPU and memory budget, which `docs/adr/0001-architecture.md` and `docs/adr/0011-sensor-service-memory-budget.md` set, `AGENTS.md` lists and `scripts/measure.ps1` checks. No project uses WPF (`RepoRulesTests`), and the window's memory, WebView2's included, is its own process's, so closing it frees all of it.

## Where to change what

| To change | Look in |
| --- | --- |
| Which sensor feeds a metric | `src/Thermi.Core/Sensors/SensorMap.cs`, then a recording test |
| What the catalog says about a sensor | `src/Thermi.Core/Sensors/SensorCatalog.cs` |
| How the fan finder decides | `src/Thermi.Core/Sensors/FanFinder.cs`, then a series test |
| How a value reads (units, digits, °F) | `src/Thermi.Core/Look/ReadingFormatter.cs` |
| A text a user reads, or a language | `src/Thermi.Core/Translations/Texts.resx` and each translation beside it, with its enum member in Core |
| When an overlay shows or hides | `src/Thermi.Core/Placement/OverlayVisibility.cs`, fed by `src/Thermi/Overlay/OverlayHost.cs` |
| What a tray icon shows | `src/Thermi.Core/Tray/TrayScene.cs` and `src/Thermi.Core/Tray/TrayTexts.cs` |
| How a tray icon is drawn, added and retried | `src/Thermi/Tray/TrayPainter.cs` and `src/Thermi/Tray/TrayIcons.cs`, then `tests/Thermi.Windows.Tests/TrayIconsTests.cs` |
| How a hotkey is written, registered and reported | `src/Thermi.Core/Hotkeys.cs` and `src/Thermi/Overlay/HotkeyRegistry.cs` |
| The default overlay or thresholds | `src/Thermi.Core/Settings/OverlayConfig.cs` |
| A field of the settings file | `src/Thermi.Core/Settings/OverlayConfig.cs` or `src/Thermi.Core/Settings/StyleConfig.cs`, its range in `src/Thermi.Core/Settings/ConfigRules.cs`, its undo in `src/Thermi.Core/Editing/ConfigFields.cs`, then `tests/Thermi.Tests/Configs/v5.json` |
| What the window may change, and why it refuses | `src/Thermi.Core/Editing/ItemEdits.cs`, `src/Thermi.Core/Editing/PowerEdits.cs`, `src/Thermi.Core/Editing/OverlayEdits.cs` |
| An item's minimum, maximum, average, smoothing or graph | `src/Thermi.Core/Look/ItemHistory.cs` |
| The style file, or how saved styles are kept | `src/Thermi.Core/Look/StyleFile.cs`, `src/Thermi.Core/Look/SavedStyles.cs` |
| How the overlay looks | `src/Thermi.Core/Look/OverlayLook.cs` for what it draws, `src/Thermi/Painting/Painter.cs` for how, checked with `thermi preview` |
| How many overlays show, and what the overlay process answers | `src/Thermi/Overlay/OverlayHost.cs`, then `tests/Thermi.Windows.Tests/Overlay/OverlayHostTests.cs` |
| Where the overlay snaps, and when a press moves it | `src/Thermi.Core/Placement/Snaps.cs`, fed by `src/Thermi/Windows/Desktop.cs`, `src/Thermi/Overlay/OverlayMouse.cs` and `OverlayEdits.Release` |
| How the window opens and comes to the front | `src/Thermi/Ui/WindowProcess.cs`, `src/Thermi/Ui/SettingsBeacon.cs`, `src/Thermi/Ui/Web/WebWindow.cs`, then `tests/Thermi.Windows.Tests/Window/WindowProcessTests.cs` |
| What the page shows and does | `src/Thermi.Settings.Web/src`, then its Playwright tests in `src/Thermi.Settings.Web/tests` |
| What the page may read and ask of C# | `src/Thermi.Core/Bridge/IWindowBridge.cs`, answered by `src/Thermi/Ui/Web/WindowBridge.cs` and `src/Thermi/Ui/Views` |
| How the window's edits are written and undone | `src/Thermi/Ui/Editor/WindowEditor.cs`, then `tests/Thermi.Windows.Tests/Window/WindowEditorTests.cs` |
| How the window runs the fan finder and its load | `src/Thermi/Ui/Views/WindowFinder.cs`, `src/Thermi/Commands/CpuLoad.cs`, then `tests/Thermi.Windows.Tests/Window/FinderTests.cs` |
| "Start with Windows" | `src/Thermi/Ui/StartWithWindows.cs`, `scripts/install.ps1`, then `tests/Thermi.Windows.Tests/Window/StartWithWindowsTests.cs` |
| Saved styles, export and import | `src/Thermi/Ui/Views/WindowLooks.cs`, `src/Thermi.Core/Look/SavedStyles.cs`, `src/Thermi.Core/Look/StyleFile.cs`, then `tests/Thermi.Tests/Acceptance/SavedStyleTests.cs` |
| How the settings file is read and written by two processes | `src/Thermi/Overlay/ConfigStore.cs`, then `tests/Thermi.Windows.Tests/Acceptance/SettingsFileWindowsTests.cs` |
| What the service reads | `src/Thermi.SensorHost/LhmSensorSource.cs` |
| What `thermi-sensors` does with its arguments | `src/Thermi.SensorHost/Program.cs`, `src/Thermi.SensorHost/SensorCommands.cs`, then `tests/Thermi.Windows.Tests/SensorHost/SensorProgramTests.cs` |
| How the overlay stays above the taskbar | `src/Thermi/Windows/Topmost.cs`, then `tests/Thermi.Windows.Tests/OnTop/TopmostTests.cs` |
| What an agent's command answers | `src/Thermi.Core/Agents`, then `tests/Thermi.Tests/Acceptance/ContractTests.cs`; the Windows side in `src/Thermi/Agents`, then `tests/Thermi.Windows.Tests/Acceptance/AgentCommandTests.cs` |
| Install and uninstall | `scripts/install.ps1` |

## Next

The settings window runs in a process of its own and shares the settings file with the overlay, plus the messages of `docs/adr/0009-overlay-and-window-messages.md`: `docs/specs/002-settings-window`, `docs/adr/0005-settings-window-process.md` and `docs/adr/0007-settings-window-in-webview2-and-react.md`.
