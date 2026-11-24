# 0001. Core, an unelevated app, an elevated sensor host

**Status**: Accepted; amended by `docs/adr/0005-settings-window-process.md` and `docs/adr/0007-settings-window-in-webview2-and-react.md` (the settings window), `docs/adr/0010-driver-and-service-from-the-window.md` (the elevated mode) and `docs/adr/0011-sensor-service-memory-budget.md` (the budget). Each says what it replaces here.

## Context

thermi must stay light (it runs all day, next to games) and easy to change for anyone who joins later. The usual ways such projects decay: logic tangled with the window and the hardware library, layers that leak, and files that grow to thousands of lines. Splitting into many projects early makes that worse, not better.

Reading CPU and motherboard sensors needs admin rights (see `docs/adr/0002-sensors.md`). But AI agents will drive thermi through its CLI and MCP server, and nothing an agent talks to should run with admin rights.

## Decision

Three projects, two processes:

- **`Thermi.Core`** (`net10.0`, no dependencies): readings, formatting, thresholds, the overlay layout as plain data, the config with its version and migrations, and design suggestions. Its target framework has no Windows APIs, so the compiler keeps Windows out.
- **`Thermi.SensorHost`** (`net10.0-windows`, elevated): the only process with admin rights and the only one that loads LibreHardwareMonitor. It reads sensors and publishes readings over a named pipe that only the signed-in user can open. It accepts no commands beyond "send readings", and ignores any request it does not understand.
- **`Thermi`** (`net10.0-windows`, unelevated, as the user): the overlay window, the CLI, the MCP server and the settings window. `Program.cs` is the only place that creates and wires parts; nothing else looks services up. Whether the CLI and MCP need their own small console executable is decided when they are built.
- **`Thermi.Tests`**: a few behavior tests at the edges.

Inside the app:

- **The overlay** is a Win32 layered window (topmost, no activation, no taskbar button). It paints the layout from Core with GDI+ into a 32-bit bitmap and shows it with `UpdateLayeredWindow`. The same painting code writes PNGs for `thermi preview` and for tests.
- **The settings window** is WPF, loaded only when it opens. It gets its own ADR when it is built. (Replaced: it is a process of its own, ADR 0005, showing a page in WebView2, ADR 0007; no process of thermi's loads WPF.)
- Contracts between processes and projects are typed. A change that breaks one fails the build.
- The saved config has a version number. Each version has a migration and a round-trip test with a real file from that version. No automatic polymorphic JSON in anything saved to disk: it breaks silently between versions.

How the sensor host starts elevated without a prompt at every sign-in gets its own ADR when it is built.

## Budget

"Light" is a number, measured from 0.1 on and shown in the stats dashboard. Starting targets, with a 1 second refresh: the overlay under 40 MB of private memory and under 0.5 % of one CPU core when idle, and the sensor host under 30 MB. Raising a target needs a note in the PR that explains why.

Amended with version 0.9: the overlay's memory target is 48 MB. The installed overlay of spec 002, read with no window open on the author's PC, held 42 MB private through two minutes of 5 s samples, 45 minutes after it started, 42.7 MB at 50 minutes and 42.2 MB at 70, with 581 handles, 17 threads and 103 modules: flat, so nothing it keeps grows with time. Spec 002 added what the overlay loads on every start (the tray's menus and notices, the window's hotkeys and gestures, Serilog, System.CommandLine), and the .NET runtime with GDI+ is most of the rest. `scripts/measure.ps1` fails past 48 MB, which leaves room for a bigger PC's sensors.

## Rejected

- **One elevated process for everything**: simplest, but the CLI and the MCP server an agent drives would run with admin rights.
- **WPF or WinUI for the overlay**: tens of MB of memory and a composition engine to show a few numbers.
- **Direct2D**: better text on transparent backgrounds, but more code to own. We revisit it if GDI+ text quality is not good enough.
- **A project per layer** (Sensors, Bar, Settings): more projects, and the boundaries we need are the ones above.

## Consequences

- Most logic lives in Core and is tested without Windows, hardware or admin rights.
- Adding a metric means reading it in the sensor host and adding data in Core; the overlay only paints.
- The pipe between the two processes is a security boundary: it gets tests for malformed and oversized input.
- GDI+ text on a transparent background needs grayscale antialiasing (ClearType does not work there).
