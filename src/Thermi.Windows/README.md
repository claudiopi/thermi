# Thermi.Windows

`thermi.exe` and `thermi-cli.exe`: the overlay, the tray, the settings window and the agents' door. It runs as the user, never with admin rights, but for `thermi elevated <verb>`, which only `thermi-elevated` starts, as SYSTEM, for one of the window's two fixes (`Commands/Elevated.cs`). It decides as little as it can: what to show, what an edit may do and what an agent gets back are Core's. This project is the Windows side of those decisions: Win32 windows, GDI+, WebView2 and the pipe; the settings file is `src/Thermi.App`'s.

One `thermi.dll` serves three processes. `thermi` is the overlay, one per user. `thermi settings` is the settings window, in a process of its own that closes with it (`docs/adr/0005-settings-window-process.md`). `thermi-cli` is the same program behind a console apphost, so a shell waits for it (`docs/adr/0013-agents-cli-and-mcp.md`).

| Folder | What lives there |
| --- | --- |
| `Overlay` | The overlay process: its hidden host window, one layered window per overlay, the mouse, the hotkeys and the pipe's client. |
| `Painting` | Draws what Core's `OverlayLook` says with GDI+, for the screen, `thermi preview`, "Copy as image" and the window's preview alike. |
| `Gestures` | What a drag shows on Windows' desktop, its ghost, snap marks and cursor, and the hotkeys' presses. |
| `Tray` | The tray icons, their painter and the overlay's menus. |
| `Win32` | Thin wrappers over Win32 calls: monitors and taskbars, staying on top, hidden windows, the app in front, crash restart. |
| `Win32/Clipboard` | "Copy as image" and copying text. |
| `Install` | Which sensor pipe to trust, the per-user sensor host, and the move to a machine install. |
| `Updates` | The overlay's and the window's side of an update: the quiet moment, the hand-over, About's check. |
| `Logging` | The events of Windows' own code, each a `[LoggerMessage]`, and the Event Log the elevated mode writes to. |
| `Commands` | The command line, `thermi preview` and the elevated mode's two fixes. |
| `Agents` | What `thermi-cli`'s commands, `src/Thermi.Cli`'s, reach on Windows: the pipe, the service's registration, the overlay's mutex, the Run key, the monitors. |
| `Ui` | The settings window's process, its beacon, "Start with Windows" and the elevation check. |
| `Ui/Web` | The Win32 window that hosts the page in WebView2, and the C# side of the bridge. |
| `Ui/Views` | What the bridge reads beyond plain edits on Windows: previews, looks, monitors. |

Entry points worth knowing:

- `Program.cs` reads the command line and starts the overlay, the window or an agent's command, which `src/Thermi.Cli` runs.
- `Overlay/OverlayHost.cs` runs the overlay's message loop, its once-a-second tick and every paint.
- `Overlay/SensorClient.cs` reads snapshots from the pipe it trusts, for the overlay and the window.
- `Ui/Web/WindowBridge.cs` answers the page's calls.

Its tests are in `tests/Thermi.Windows.Tests`; the page's own are in `src/Thermi.Settings.Web/tests`.
