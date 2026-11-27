# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it through the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus; the other desktops' surfaces and `thermi settings` come with their tasks of `docs/specs/006-linux/tasks.md`. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's. Under `--trace`, the overlay writes on its standard error the events the tests wait for, one per line, as `tests/Thermi.Linux.Tests/Traced.cs` names them. On its bus name, `io.github.claudiopi.thermi.Overlay`, it exports one action, `quit`, through `org.gtk.Actions`: the settings window's "Exit thermi".

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, against compositors of their own, are in `tests/Thermi.Linux.Tests`.

| Folder | What lives there |
| --- | --- |
| `Commands` | `thermi`'s commands on Linux beside the shared ones, as `thermi preview`. |
| `Desktop` | What the overlay reads from the desktop and the machine: high contrast, the panels, the pointer's drag distance and double-click time, the CPU's base clock; and its notices. |
| `Gnome` | thermi's side of the D-Bus protocol with its GNOME Shell extension, the Shell's monitors as Core's screens, and the extension's arrival. |
| `Logging` | The overlay's log events. |
| `Overlay` | The overlay's host on one GLib main loop: each overlay painted, placed, dragged and dropped with its snap marks, its menu, and the `quit` it exports. |
| `Packaging` | The systemd user unit that runs the overlay in the person's graphical session. |
| `Painting` | The overlay painted with Cairo and Pango, from Core's look, layout and shapes, as Windows' GDI+ painter does. |
| `Sensors` | The sensor service's socket, which App's reader reads. |

## What the tests rely on

The tests drive thermi as a person's desktop does and wait for what it says it did, never for a time, so these are a contract: a change here changes `tests/Thermi.Linux.Tests` with it.

Under `--trace`, each event is one line on the standard error, `<seconds on CLOCK_MONOTONIC, 3 decimals> <event>[ <about>]`, as `812.034 shown`; a line of another shape is the log's.

| Event | Written when |
| --- | --- |
| `paint` | A second of the service's was taken and painted, whether an overlay is on screen or not. |
| `shown` | Every overlay not hidden is on screen as painted: once at start, even with none, as soon as they show the service's first second or that it is stopped; and each time they come back after `hidden`. |
| `hidden` | Hide overlays took them off the screen. |
| `edit-applied` | One change of the settings is saved and on screen, wherever it came from. |
| `dropped` | A drag ended, let go or taken back with Esc, and what it left is saved and on screen. |

Notices are the desktop's own freedesktop `Notify`, also on GNOME, where the Shell serves it; a test matches their buttons by action id, never by text.

| Notice | Action ids |
| --- | --- |
| Overlays hidden | `show-it` or `show-them`, and `got-it` |
| Click-through on | `undo` and `got-it` |
| GNOME's extension missing | `install-extension`, which asks the Shell to install it again |
| GNOME's extension of another protocol | `update-extension`, which asks the Shell for its updates |

The extension's `metadata.json` says its protocol's version as `thermi-protocol`, which thermi checks against its own before it shows anything through it. `thermi-sensors --console --root <tree>` serves a recorded tree in the session, on the socket in its runtime folder, which thermi reads before `/run/thermi/sensors.sock`; a tree of one second, served every second the same, holds a whole screen still.
