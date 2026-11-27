# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`). The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it on the desktop's surfaces (`IOverlaySurfaces`): through the GNOME Shell extension of `src/Thermi.Gnome` over D-Bus, as GTK windows made layer surfaces on KDE Plasma, Sway and Hyprland, or as GTK windows kept above on X11. A surface only shows pictures and reports the pointer; the presses, drags and drops are `Thermi.App`'s, as on Windows. `thermi settings` comes with its tasks of `docs/specs/006-linux/tasks.md`. `thermi-cli` runs `Thermi.Cli`'s commands over an `IAgentEnvironment` of Linux's. Under `--trace`, the overlay writes on its standard error the events the tests wait for, one per line, as `tests/Thermi.Linux.Tests/Traced.cs` names them. On its bus name, `io.github.claudiopi.thermi.Overlay`, it exports one action, `quit`, through `org.gtk.Actions`: the settings window's "Exit thermi".

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, against compositors of their own, are in `tests/Thermi.Linux.Tests`.

| Folder | What lives there |
| --- | --- |
| `Agents` | What `thermi-cli`'s commands reach on Linux: the overlay's user unit for the start at sign-in, and every other answer of `IAgentEnvironment`. |
| `Commands` | `thermi`'s commands on Linux beside the shared ones, as `thermi preview`. |
| `Desktop` | What the overlay and the agents' commands read from the desktop and the machine: which surfaces the session has, the session's D-Bus and the objects thermi serves on it, the monitors as GNOME and GDK lay them out, high contrast, the panels, the pointer's drag distance and double-click time, the CPU's base clock; the desktop's notifications, and the GTK window a picture shows in off GNOME. |
| `Gnome` | thermi's side of the D-Bus protocol with its GNOME Shell extension, the Shell's monitors as Core's screens, the extension's arrival, and the extension as the overlay's surfaces, its icons in the top bar and its hotkeys. |
| `Hotkeys` | thermi's four hotkeys, through GNOME's extension, the GlobalShortcuts portal or X11's grab; one another program holds is refused. |
| `LayerShell` | The overlay's surfaces on a Wayland compositor with layer surfaces, through `gtk4-layer-shell`, and the surface that measures each monitor's work area. |
| `Logging` | The overlay's log events. |
| `Overlay` | The overlay's host on one GLib main loop: each overlay painted and placed, the pointer on it handed to `Thermi.App`'s press, the ghost and snap marks a drag shows, its menu, and the `quit` it exports. |
| `Packaging` | The systemd user unit that runs the overlay in the person's graphical session. |
| `Painting` | The overlay and the tray's icons painted with Cairo and Pango, from Core's look, layout and shapes, as Windows' GDI+ painter does. |
| `Tray` | thermi's icons as Core's tray decides them, in GNOME's top bar or as StatusNotifierItems with their dbusmenu menus, and its notices as the desktop's notifications, each button doing what it does on Windows. |
| `Sensors` | The service's socket: the system's, else where it is missing the user's own, of a `thermi-sensors --console`, each trusted only from who should serve it; `THERMI_SENSORS_SOCKET` names one to read alone. |
| `X11` | The overlay's surfaces on X11: GTK windows kept above, off the taskbar and the pager, the work area from the panels' struts, the pointer read for Fade, and thermi's hotkeys as X11's own grabs. |

The `thermi` package's files, beside the code in `Packaging`, are the overlay's user unit, its launcher's entry, the package's `nfpm.yaml` and scripts, and the AUR's `PKGBUILD` for both packages; `scripts/linux-packages.ps1` packs them with the overlay, its icons and the GNOME Shell extension.

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
| `hotkey-refused <role>` | The desktop refused a hotkey, held by another program, by its field's name in the settings, as `nextStyle`. |

| `lifted` | A hold lifted a value or a pill, and its ghost is on screen under the pointer. |
| `faded` | An overlay with Fade near the pointer is on screen at its faintest, the pointer over it. |

Notices are the desktop's own freedesktop `Notify`, also on GNOME, where the Shell serves it; a test matches their buttons by action id, never by text.

| Notice | Action ids |
| --- | --- |
| First start | `first-run`, which opens the settings |
| An item critical, with Notify me | `critical` and `got-it` |
| Overlays hidden | `show-it` or `show-them`, and `got-it` |
| Click-through on | `undo` and `got-it` |
| GNOME's extension missing | `install-extension`, which asks the Shell to install it again |
| GNOME's extension of another protocol | `update-extension`, which asks the Shell for its updates |
| No surface to show the overlays on, once ever | `no-overlay`, which only closes it |

The extension's `metadata.json` says its protocol's version as `thermi-protocol`, which thermi checks against its own before it shows anything through it. `thermi-sensors --console --root <tree>` serves a recorded tree in the session, on a socket of the test's own, which `THERMI_SENSORS_SOCKET` names for thermi to read alone; a tree of one second, served every second the same, holds a whole screen still.
