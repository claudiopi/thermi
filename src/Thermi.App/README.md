# Thermi.App

What the overlay and the settings window decide beyond Core that needs a package, as the logs do, or the file system and its waits, which any OS's host shares: the settings file every process writes and the settings as a host holds them, the fan finder's run, what a desktop gesture leaves, and the settings window's edits and readings. It targets plain `net10.0`, so no Windows type gets in (`RepoRulesTests`), and its types are internal, seen by the hosts and the tests its project names. Core keeps what needs neither; `docs/adr/0018-shared-and-windows-projects.md` says how a host for another OS takes the rest.

| Folder | What lives there |
| --- | --- |
| `SettingsFile` | `config.json` read and written under its mutex, the settings a host holds over it, the edits that wait to be written, and watching the file. |
| `Finder` | The fan finder's run, the window's and an agent's alike, and the CPU load it asks for, in a process of its own. |
| `Gestures` | What a desktop gesture leaves for every overlay: where a drop joins the overlay under it, and the last gesture's undo. |
| `Window` | The settings window's bridge, which answers the page alike on every OS and asks its host, `IWindowHost`, for the rest: its edits and undo, the sensors' readings and charts it keeps, the views, previews, places and looks. |
| `Painting` | What every OS's overlay painter answers the window, `IOverlayPainter`, and how an overlay is fitted to a room or its monitor. |
| `Logging` | The user's logs in their local folder, the agents' log read back for the window, the events of what lives here, each a `[LoggerMessage]`, and the user's folders. |

Its tests run where its hosts are: `tests/Thermi.Windows.Tests` drives it with real files, other writers and the clock.
