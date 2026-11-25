# 005. FPS and frame time of the game in front

**Status**: approved

## Why

People who play want to see their frame rate beside their temperatures, and how smooth it is: a steady line is fluid, spikes are stutters. It is the one thing other overlays show that thermi does not. thermi already has a service with the rights to see it (`docs/adr/0003-sensor-service.md`); how it reads frames, and why that way, is `docs/adr/0014-frames-from-etw.md`.

## Requirements

Reading

- R1. Two metrics read the app in front: FPS (`game.fps`), the frames it presented in the last second, as a whole number; and frame time (`game.frametime`), the longest gap between two of its frames that ended in the last second, in milliseconds with one decimal.
- R2. The app in front is the program of the foreground window, or, while a window of thermi's is in front, of the window under it. A program whose frames come from a process it started, as a browser's, counts as itself.
- R3. Frames are read for Direct3D 9 to 12, Vulkan and OpenGL, windowed, borderless and in exclusive full screen, on any GPU, with frame generation's frames counted.
- R4. Within 2 s of another app coming to the front, the values are of that app.
- R5. An app in front that stops presenting, as a game while it loads, reads 0 FPS and a frame time that grows with the wait up to 999.9 ms, a freeze, for 5 s; after that it presents nothing.
- R6. While the app in front presents nothing (the desktop, a document), FPS and frame time items take no room in the overlay, and a pill left with nothing shows nothing. While frames cannot be read at all (a service from before, no trace session free), they take no room in the overlay either, so no taskbar shows an FPS of `–` all day, and the window shows them as `–` with why.

Items

- R7. The window offers FPS and Frame time under a device of their own, Game, in the catalog and among the items to add, each with its history, chart, graph and tray icon as any item, labeled FPS.
- R8. FPS warns when it is low: by default a warning under 60 and critical under 30, which the person changes or removes in the same chart, with the critical line below the warning. Frame time warns when it is high: by default a warning at 50 ms and critical at 100 ms.
- R9. A new settings file's first overlay ends with FPS, so a game shows it with nothing set up. Settings files that exist are left as they are.

The service

- R10. The service reads frames only while a client is connected, and stays within its budget (`docs/adr/0011-sensor-service-memory-budget.md`), 3 % of one core and 80 MB, while a game presents at 144 FPS.
- R11. The pipe stays one-way, and no process's name leaves the service: frames go out as process ids and numbers.
- R12. `thermi-sensors --frames <file> --seconds <n>`, as administrator, records each present of every process, with ids and times and no names, for tests and bug reports.

## Design

**The service** (a new folder, `Frames`, of `src/Thermi.SensorHost`) runs one real-time ETW session, `thermi-frames`, through CsWin32, while a client is connected, enabling DXGI's and Direct3D 9's present events and the graphics kernel's for the rest, on a thread of its own that runs `ProcessTrace` (ADR 0014). Each present goes to Core's `FrameTally` (a new folder, `Frames`, of `src/Thermi.Core`), which each second gives, per process, its FPS and its frame time, and keeps a process 5 s at 0 FPS after its last present. A runtime's present reaches the kernel too, so the kernel's count only for a process that sent no runtime's in the last 5 s, as an OpenGL or Vulkan driver that presents on its own. `FramedSource` wraps the sensors' read and adds the tally's 8 busiest processes, `dwm.exe` left out, as fixed slots of sensors under the hardware type `Frames`: process id, parent's id, FPS, frame time. The slots are always there, without values when the session could not start, so the catalog line never changes with them. The session starts with the first read and stops 5 s after the last. `--frames` writes the presents a session of its own sees, as JSON lines, through the same tally's input, so a recording never stops the service's.

**The readers.** Core's `GameSensors.With(snapshot, front)`, beside `SystemSensors.With`, drops the slots and adds the game's two sensors, `/game/0/framerate/0` and `/game/0/frametime/0`, with the values of the slot whose process, or parent, is `front`, the process in front, and without values when no slot is (the catalog says why: frames that cannot be read, or nothing presenting in front). The overlay and the window give it the process in front on their tick: the foreground window's (`GetForegroundWindow`, `GetWindowThreadProcessId`), and when that is thermi's own process, the next shown window below it in Z order of another program (`GetWindow` with `GW_HWNDNEXT`); the desktop, the taskbar and File Explorer give none, since Explorer starts nearly every app and a game behind it would match by its parent (`src/Thermi/Windows/FrontApp.cs`, in `SystemFeed`). Everything after takes the game's sensors as any: `SensorMap` maps the two metrics to them, the catalog lists them under Game, the history and lines keep them.

**Kinds and lines.** Core gains the sensor kinds `FrameRate` (frames per second, no decimals) and `FrameTime` (milliseconds, one decimal), `DeviceKind.Game` and `MetricLabel.Fps`. `Threshold` learns that a kind warns low: for `FrameRate` a warning is at or above its critical, and a value at or under a line reaches it; the file's shape does not change, so no settings version is added, and `ConfigRules` checks the order by the kind. Their lines run up to 1000. The window's chart (`src/Thermi.Settings.Web/src/chart`) draws and drags a low kind's lines as it is told by the bridge, the critical below. The texts, Game, FPS, Frame time and their descriptions, join the translations.

**Defaults.** A new settings file's first overlay, `OverlayConfig.First` in `ThermiConfig.New`, is `OverlayConfig.DefaultItems` with FPS after; it takes no room until a game is in front (R6), so the overlay a new user sees does not change. `DefaultItems` itself, what an added overlay and the tests start from, does not change.

**Files that change**: the new folders above; `src/Thermi.Core/Sensors/Metrics.cs`, `Reading.cs`, `SensorMap.cs`, `SensorCatalog.cs`; `src/Thermi.Core/Settings/Threshold.cs`, `ConfigRules.cs`, `OverlayConfig.cs`; `src/Thermi.Core/Look/ReadingFormatter.cs`; `src/Thermi.SensorHost/Program.cs`, `SensorCommands.cs`, `SensorHostEnvironment.cs`, `NativeMethods.txt`; `src/Thermi/Windows/SystemFeed.cs`, which both the overlay and the window read through, and `src/Thermi/Overlay/ConfigStore.cs` for a new file; the chart; `AGENTS.md`'s commands; `docs/ARCHITECTURE.md` and `docs/QUICKSTART.md`.

**Testing.** The edges are a recording of presents in and what the overlay shows out. A recording is made with `--frames` while the author rides Zwift, his one game, an OpenGL one: into a world, through its loading screen, alt-tabbing to a browser and the desktop and back; trimmed to the game's process and a few others, it replays through `FrameTally` into snapshots, and those through `GameSensors` and the layout with the process in front given, which is the acceptance of R1, R2, R4, R5, R6 and R9 in one table. The low lines are rows of the thresholds' existing table. The session itself, the APIs and the cost are checked by hand on the author's PC: Zwift against its own counter, and the other APIs with free demos that present through them (Vulkan's `vkcube`, Direct3D 9, 11 and 12 samples), against PresentMon run beside them.

**Test budget.** This spec adds at most 10 unique tests; most of its cases are rows of tables that exist.

**Rejected:** a frame-time graph of every frame, as some overlays draw, sent from the service: the pipe and the history are one value a second, and the longest frame of each second already shows every stutter as a spike in the item's line, at no cost.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
