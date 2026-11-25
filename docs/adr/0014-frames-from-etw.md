# 0014. FPS and frame time from the presents Windows traces

**Status**: Proposed

## Context

Spec 005 shows the frames per second and the frame time of the game in front, the one thing gamers miss in thermi. Windows has no API for another process's frame rate. Every monitor that measures it without hooking into the game, PresentMon and the overlays built on it, reads the events DirectX and the graphics kernel trace through ETW each time a program presents a frame. Starting such a trace session needs administrator rights or the Performance Log Users group; the overlay has neither, and the `thermi-sensors` service, LocalSystem, has them (`docs/adr/0003-sensor-service.md`).

Three ways were weighed:

- **PresentMon's console** (Intel, MIT, `PresentMon-2.6.0-x64.exe`, 1 MB, x64 only since 2.5), run by the service with `--output_stdout --no_csv`, writing a CSV line per frame of every process.
- **PresentMon 2's service and API**: a second service, installed with Intel's 160 MB MSI or redistributed, with its own pipe.
- **An ETW session of thermi's own** in the service, made with CsWin32 like every Win32 call of thermi's, enabling the few events that mark a present.

## Decision

- **The service traces presents itself.** While a client is connected, as it reads sensors, `thermi-sensors` runs one real-time ETW session, `thermi-frames`, and stops it 5 s after its last read, once the last client has left; a session of that name left by a crash is stopped and started again, and one stopped from outside is started again. It enables only the present events of the APIs games use: DXGI's `Present` start (Direct3D 10, 11 and 12, and Vulkan or OpenGL presented through DXGI, as current drivers do by default), Direct3D 9's `Present` start, and the graphics kernel's (`Microsoft-Windows-DxgKrnl`) present history, plain and detailed, which Vulkan and OpenGL drivers that present on their own reach, and its `BlitRect`, the copy of a frame into its window: Zwift, an OpenGL game, tells of its frames by the detailed history while riding and by one `BlitRect` a frame alone in its menus. Each source is counted apart, and each second a process's count is the source that told of most, so a frame told by two counts once and a change of path costs no second. Each event gives the process and its time; nothing else of an event is read, and any other event is dropped. One thread of its own runs `ProcessTrace`, as ETW requires; it hands each present to the second's tally and never blocks the reading.
- **What it counts** is the frames a program presents, PresentMon's `MsBetweenPresents`, which is what in-game counters show: frames the game made, not those the display changed. Frame generation counts, since its frames are presented too.
- **What it sends.** Each second, per process: the frames presented in that second over its length, and the longest gap between two presents that ended in it, so a stutter shows as one long frame instead of vanishing into an average. A process that stops presenting stays 5 s more at 0 FPS, its frame time growing to 999.9 ms, a freeze, then leaves. The math lives in Core (`FrameTally`), fed in tests by recordings of presents.
- **Which process is shown** is the overlay's call, because the service runs in session 0 and cannot see any session's foreground window, and the pipe stays one-way (`docs/adr/0012-sensor-pipe-catalog-once.md`). The service sends the 8 processes that presented most in the last second, `dwm.exe` left out, as fixed slots of sensors: each slot's process id, its parent's id, its FPS and its frame time, as numbers. The catalog never changes with them, so the pipe keeps sending values alone, about 300 bytes more a second. Each process reading a snapshot (the overlay, the window, the CLI) takes the foreground window's process, or the one below thermi's own windows in Z order when thermi's is in front, matches it to a slot by its id or as its parent (a browser presents from a child process), and turns that slot into the two sensors of a device of thermi's own, the game, as `SystemSensors` adds the PC's: everything after (the catalog, items, history, lines) takes them as any sensor. The slots themselves never leave the reader.
- **Nothing in front that presents** is not a missing value: the game's sensors read nothing, and items made from them take no room. A service that could not start its session sends the slots with no values, and one from before sends none; the game's sensors then read not a number, so the items take no room in the overlay either, no taskbar showing an FPS of `–` all day, and the window says frames cannot be read.
- **The recordings.** `thermi-sensors --frames <file> --seconds <n>`, as administrator, writes each present (process, parent, time in ms from the start, and the event that told of it) as a JSON line, with the processes' names replaced by numbers, from a session of its own so it never stops the service's: the tests replay those through Core as the service tallies them live.
- The cost stays inside the service's budget, 3 % of one core and 80 MB (`docs/adr/0011-sensor-service-memory-budget.md`), with a game presenting at the monitor's rate; `scripts/measure.ps1` measures it with one running, by hand.

## Rejected

- **PresentMon's console**: it handles every present mode Intel has met, which is its strength, but the service would run a third-party program as SYSTEM, kept current by hand, with no Arm64 build, in a process `scripts/measure.ps1` would not count against the service's budget, and the service would parse a CSV line per frame of every process presenting, the browser and the desktop's included. If recordings show thermi's own count wrong for an API, this is the fallback, with an ADR of its own.
- **PresentMon 2's service**: a second service and pipe to install and trust, for data thermi needs a small part of.
- **TraceEvent** (Microsoft, MIT): it parses any event, at the price of a large library loaded into a service already near its memory budget, to read two fields of three events.
- **A slot for every process that presents**, named by it: the catalog, 38 KB, would go out again each time an app starts or stops animating.
- **The busiest process alone**: a video on a second monitor would beat a game capped at 30 FPS.

## Consequences

- Present-mode subtleties (a frame dropped before the screen, composed versus independent flip) do not show: thermi counts presents, as in-game counters do. PresentMon's displayed FPS can differ.
- A game in exclusive full screen draws over every window, so the overlay cannot show there; the FPS still reads in the tray icon and the window.
- An app framed by another process's window, as old UWP apps by `ApplicationFrameHost.exe`, matches no slot and shows nothing.
- Any interactive user can read the pipe, so they can see which process ids present how fast in another user's session: ids alone, which `tasklist` already shows them.
- The DxgKrnl events change between Windows versions more than DXGI's; their part, which OpenGL games like Zwift may take, is checked with Zwift and a Vulkan demo by hand on each major Windows update.
- ETW allows 64 sessions on a PC; if none is free, the service logs it to the event log and sends the slots empty, and tries again a minute later.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md` says the service reads sensors through LibreHardwareMonitor alone. It also traces presents through ETW, only while a client is connected.
