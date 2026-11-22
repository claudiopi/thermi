# Architecture

How thermi is built, with the real names, so you can find any piece and change it without breaking the design. New here? Read [QUICKSTART.md](QUICKSTART.md) first. Each project's own `README.md` has its details; the reasons are in `docs/adr`.

## The system on one page

```
 Hardware (CPU, GPU, motherboard, memory)
        │ LibreHardwareMonitor, with the PawnIO driver
        ▼
 ┌─ thermi-sensors (src/Thermi.SensorHost) ── Windows service, LocalSystem ────┐
 │ LhmSensorSource reads ─► SensorPipeServer writes one JSON line per second    │
 └──────────────────────────────┬──────────────────────────────────────────────┘
                                ▼ named pipe "thermi-sensors": read-only, owner checked
 ┌─ thermi.exe (src/Thermi) ── the user's process, no admin rights ────────────┐
 │ SensorClient ─► LatestSnapshot ─► SensorMap ─► OverlayLayout ─► Painter      │
 │                                    (Core)        (Core)          (GDI+)      │
 │ OverlayWindow: layered Win32 window · Topmost keeps it above the taskbar    │
 │ Settings + ConfigStore ◄─► %APPDATA%\thermi\config.json (watched)           │
 └──────────────────────────────────────────────────────────────────────────────┘
```

## Projects

| Project | Runs as | Depends on | Job |
| --- | --- | --- | --- |
| `src/Thermi.Core` | a library | nothing | Snapshots, metrics, sensor mapping, formatting, thresholds, config and layout. |
| `src/Thermi.SensorHost` | `thermi-sensors.exe`, a service | Core, LibreHardwareMonitor | Reads the sensors and serves snapshots. |
| `src/Thermi` | `thermi.exe`, the user | Core, System.Drawing.Common | The overlay, its settings file and `thermi preview`. |
| `tests/Thermi.Tests` | the test runner | Core | Behavior from recordings, file formats and the repo rules. |
| `tests/Thermi.Windows.Tests` | the test runner | all of the above | Real pipes, the service, the painter and the settings file. |

## Names

| Name | What it is |
| --- | --- |
| **Snapshot** | Every sensor of the types thermi shows, at one moment: id, hardware, type, name, value. `Snapshot.cs`. |
| **Recording** | A snapshot saved with `thermi-sensors --dump`, replayed by tests. `tests/Thermi.Tests/Recordings`. |
| **Metric** | A stable name for what the overlay shows, like `cpu.temperature`. `Metrics.cs`. |
| **Sensor map** | The rules from a snapshot to each metric, and the "zero means not read" rule. `SensorMap.cs`. |
| **Item** | One metric in the settings, with an optional meter and threshold. `OverlayConfig.cs`. |
| **Pill** | A group of items under one label, painted as one rounded shape. `OverlayLayout.Groups`. |
| **Room** | The widest value an item can show, the width kept for it. `ReadingFormatter.Widest`. |
| **Freshness** | A snapshot older than 3 s counts as gone. `LatestSnapshot` in `SnapshotLines.cs`. |

## Rules of the road

- **Trust.** The service is the only elevated process and never reads from its pipe. The overlay trusts a pipe only when its owner is SYSTEM or Administrators (`docs/adr/0003-sensor-service.md`).
- **Failure.** A failure in the service stops it with an error, and Windows restarts it. Nothing the overlay reads can crash it; errors go to `%LOCALAPPDATA%\thermi\thermi.log`.
- **Time.** Code gets time from an injected `TimeProvider`, so tests run on a fake clock.
- **Above the taskbar.** `Topmost.cs` reacts when a taskbar rises above the overlay, and the once-a-second tick re-raises as a safety net (`docs/adr/0004-above-the-taskbar.md`).
- **Settings.** The file is versioned. A broken or newer file is never overwritten: the overlay keeps the last settings it read well, or the defaults if there were none, and says why.
- **Budget.** Spec 001 sets the target: at rest, the overlay under 0.5 % of one core and 40 MB of private memory.

## Where to change what

| To change | Look in |
| --- | --- |
| Which sensor feeds a metric | `src/Thermi.Core/SensorMap.cs`, then a recording test |
| How a value reads (units, digits) | `src/Thermi.Core/ReadingFormatter.cs` |
| The default overlay or thresholds | `src/Thermi.Core/OverlayConfig.cs` |
| How the overlay looks | `src/Thermi/Painter.cs`, checked with `thermi preview` |
| What the service reads | `src/Thermi.SensorHost/LhmSensorSource.cs` |
| Install and uninstall | `scripts/install.ps1` |

## Next

The settings window runs in a process of its own and shares the settings file with the overlay, plus two window messages: `docs/specs/002-settings-window` and `docs/adr/0005-settings-window-process.md`.
