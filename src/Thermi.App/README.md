# Thermi.App

What the overlay and the settings window decide beyond Core that needs a package, as the logs do, or the file system and its waits, which any OS's host shares: the settings file every process writes and the settings as a host holds them, the sensor service's client, the fan finder's run, what a desktop gesture leaves, and the settings window's edits and readings. It targets plain `net10.0`, so no Windows type gets in (`RepoRulesTests`), and its types are internal, seen by the hosts and the tests its project names. Core keeps what needs neither; `docs/adr/0018-shared-and-windows-projects.md` says how a host for another OS takes the rest.

| Folder | What lives there |
| --- | --- |
| `SettingsFile` | `config.json` read and written under its mutex, the settings a host holds over it, the edits that wait to be written, and watching the file. |
| `Sensors` | The client of the sensor service every OS's overlay, window and `thermi-cli` share: it connects, trusts or refuses who serves, reads the snapshots' lines, from Windows' pipe or Linux's socket, and connects again when they stop. Each OS says how it connects. |
| `Finder` | The fan finder's run, the window's and an agent's alike, and the CPU load it asks for, in a process of its own. |
| `Gestures` | A desktop gesture on any overlay, which a surface only feeds the pointer and draws: a press that lifts, drags and resizes, the drag's drop or its snaps, a menu row's choice, and the last gesture's undo; and what a hotkey, a tray icon's press and a notice's button do on every OS. |
| `Overlay` | What an overlay decides beyond its gestures, which a desktop only draws: its value tooltips, their waits and their quiet, where Windows' tooltip control is missing, its own clock, for the critical glow's frames and OLED care's steps, and when it hands over to the files a package changed, where the system's updater changes them. |
| `Link` | Each end of the link between the overlay and the settings window: what the window is told and what it tells back, as `docs/adr/0009-overlay-and-window-messages.md` says, whatever carries it, Windows' hidden windows or Linux's session bus. |
| `Window` | The settings window's bridge, which answers the page alike on every OS and asks its host, `IWindowHost`, for the rest: its edits and undo, the sensors' readings and charts it keeps, the views, previews, places and looks. |
| `Painting` | What every OS's overlay painter answers the window, `IOverlayPainter`, and how an overlay is fitted to a room or its monitor. |
| `Preview` | What `thermi preview` does around each OS's painter: the settings and snapshots it reads, the items it paints, the PNG written whole or not at all. |
| `Logging` | The user's logs in their local folder, the agents' log read back for the window, the events of what lives here, each a `[LoggerMessage]`, the user's folders, and the events `--trace` writes for a test to wait on. |

Its tests run where each part can: `tests/Thermi.Tests`, on every OS, drives its gestures, tooltips, link and menus as a host does; `tests/Thermi.Windows.Tests` drives it with real files, other writers and the clock; and `tests/Thermi.Linux.Tests` through Linux's thermi, on desktops of the tests' own.
