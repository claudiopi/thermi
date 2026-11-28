# Thermi.Linux

`thermi` and `thermi-cli` on Linux, as the person (`docs/adr/0019-linux.md`).

The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with Cairo and Pango, and shows it on the desktop's surfaces (`IOverlaySurfaces`). On GNOME that is the Shell extension of `src/Thermi.Gnome`, over D-Bus; on KDE Plasma, Sway and Hyprland, GTK windows made layer surfaces; on X11, GTK windows kept above. A surface only shows pictures and reports the pointer: the presses, drags and drops are `Thermi.App`'s, as on Windows.

`thermi settings` opens the settings window, the page in WebKitGTK, one per session. `thermi-cli`, the same program under another name, runs `Thermi.Cli`'s commands over Linux's `IAgentEnvironment` through Gio alone, never GTK, so it answers with the sensors' package alone and over SSH.

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, against compositors of their own, are in `tests/Thermi.Linux.Tests`.

The running overlay holds the bus name `io.github.claudiopi.thermi.Overlay`, and answers its own programs there:

| Call | Who calls it, and why |
| --- | --- |
| `quit`, an action of `org.gtk.Actions` | The settings window's "Exit thermi". |
| `HotkeyStatus` | The settings window, for the hotkeys the desktop refused. |
| `Message` | The settings window, for each message of their link. |
| `Open` | A second start of thermi, to open the settings window. |
| `State` | `thermi-cli`, and the settings window for its map and where each overlay shows, as `OverlayState`: whether the overlays can show, and the monitors as the overlay places them, which only a running overlay measures. |

| Folder | What lives there |
| --- | --- |
| `Agents` | What `thermi-cli`'s commands reach on Linux: the overlay's user unit for the start at sign-in, and every other answer of `IAgentEnvironment`. |
| `Commands` | How Linux runs the shared `thermi preview`, with its Cairo painter. |
| `Desktop` | What the overlay and the agents' commands read from the desktop and the machine: which surfaces the session has, the session's D-Bus and the objects thermi serves on it, the monitors as GNOME and GDK lay them out, high contrast, the panels, the pointer's drag distance and double-click time, the CPU's base clock; the desktop's notifications, and the GTK window a picture and its menu show in off GNOME. |
| `Gnome` | thermi's side of the D-Bus protocol with its GNOME Shell extension, the Shell's monitors as Core's screens, the extension's arrival and its switch in the person's Shell settings, and the extension as the overlay's surfaces, its icons in the top bar and its hotkeys. |
| `Hotkeys` | thermi's four hotkeys, through GNOME's extension, the GlobalShortcuts portal or X11's grab; one another program holds is refused, which the settings window asks the overlay about. |
| `LayerShell` | The overlay's surfaces on a Wayland compositor with layer surfaces, through `gtk4-layer-shell`, each faded by the opacity of its window's drawing area and its menu GTK's own popover, and the surfaces and popups that measure each monitor's work area on every edge, and on KDE Plasma where its panel's launcher, task manager and tray are. |
| `Logging` | The overlay's log events. |
| `Overlay` | The overlay's host on one GLib main loop: each overlay painted and placed, the pointer on it handed to `Thermi.App`'s press, the ghost and snap marks a drag shows, its menu, the `quit` it exports, and what it answers its own programs on the session's bus, as the hotkeys the desktop refused. |
| `Overlay/Link` | The running overlay's links past its surfaces: its end of the settings window's messages over the session's bus, a second start's or the app launcher's request to open the window, the desktop's look it follows, systemd's watchdog, and the update it hands over to. |
| `Overlay/Tips` | Each value's tooltip as a picture of its own on the desktop's surfaces, where App's tooltips place it. |
| `Packaging` | What the `thermi` package installs beside the program, and how: the systemd user unit that runs the overlay in the person's graphical session, the autostart entry and Sway's config.d file that start it where no such session is, the app launcher's entry, the .deb's AppArmor profile, which gives WebKit's sandbox its user namespace on Ubuntu, the package's nfpm config and scripts, and the AUR's `PKGBUILD` for both packages, which `scripts/linux-packages.ps1` packs with the overlay, its icons and the GNOME Shell extension. |
| `Painting` | The overlay and the tray's icons painted with Cairo and Pango, from Core's look, layout and shapes, as Windows' GDI+ painter does. |
| `Tray` | thermi's icons as Core's tray decides them, in GNOME's top bar or as StatusNotifierItems with their dbusmenu menus, and its notices as the desktop's notifications, each button doing what it does on Windows. |
| `Updates` | What Linux gives App's hand-over to the files a package changed: the program's folder watched, and the session's idle time and lock as the desktop tells them; and the settings window that follows the overlay back, starting its successor. |
| `Sensors` | The service's socket: the system's, else where it is missing the user's own, of a `thermi-sensors --console`, each trusted only from who should serve it; `THERMI_SENSORS_SOCKET` names one to read alone. |
| `Window` | `thermi settings`: one GTK 4 window per session with the page in WebKitGTK, served from the install's `web` folder, the window's end of its link with the overlay over the session's bus, and the bridge's host on Linux: the portal's look, the GNOME extension's and the service's fixes behind the desktop's prompts, and the overlay's user unit; and `thermi sign-in`, which starts the overlay at sign-in where the desktop brings no graphical session for systemd. |
| `X11` | The overlay's surfaces on X11: GTK windows as docks, above every window but a full-screen one, which the root window's changes tell as the person's window goes full screen or comes back, off the taskbar and the pager, taking the pointer only through their input shape, faded and with menus as on layer surfaces; the work area from the panels' struts, the pointer read for Fade and drags, and thermi's hotkeys as X11's own grabs. |

## What the tests rely on

The tests drive thermi as a person's desktop does and wait for what it says it did, never for a time, so these are a contract: a change here changes `tests/Thermi.Linux.Tests` with it.

Under `--trace`, the overlay and the settings window write each event as one line on their standard error, `<seconds on CLOCK_MONOTONIC, 3 decimals> <event>[ <about>]`, as `812.034 shown`, with the names `tests/Shared/Traced.cs` gives them; a line of another shape is the log's.

| Event | Written when |
| --- | --- |
| `paint` | A second of the service's was taken and painted, whether an overlay is on screen or not. |
| `shown` | Every overlay not hidden is on screen as painted: once at start, even with none, as soon as they show the service's first second or that it is stopped; and each time they come back after `hidden`. |
| `hidden` | Hide overlays took them off the screen. |
| `edit-applied` | One change of the settings is saved and on screen, wherever it came from. |
| `dropped` | A drag ended, let go or taken back with Esc, and what it left is saved and on screen. |
| `hotkey-refused <role>` | The desktop refused a hotkey, held by another program, by its field's name in the settings, as `nextStyle`. |
| `hotkeys-refused` | The desktop gave none of the hotkeys: it has no way to give them, or the person said no. Said once, in place of a `hotkey-refused` for each. |
| `lifted` | A hold lifted a value or a pill, and its ghost is on screen under the pointer. |
| `faded` | An overlay with Fade, opaque until then, is on screen fainter as the pointer nears it: its surface's opacity, never a new picture. |
| `window-shown <item>` | The settings window showed or came to the front, about the item it shows, or about nothing. The window writes it. |

Notices are the desktop's own freedesktop `Notify`, also on GNOME, where the Shell serves it; a test matches their buttons by action id, never by text. Core's notices, the first start's, the hidden overlays' and click-through's, also take `default`, a click on the notice past its buttons, which does what a click on Windows' does.

| Notice | Action ids |
| --- | --- |
| First start | `first-run`, which opens the settings |
| An item critical, with Notify me | `critical`, its "Got it" |
| Overlays hidden | `show-it` or `show-them`, and `got-it` |
| Click-through on | `undo` and `got-it` |
| GNOME's extension missing | `install-extension`, which asks the Shell to install it again |
| GNOME's extension without the overlay's own calls | `update-extension`, which asks the Shell for its updates |
| GNOME's extension of an earlier protocol, which still shows the overlays | none: it asks to sign in again |
| The settings window ended with an error, which names its log | `open-again`, which opens it again |
| No surface to show the overlays on, once ever | `no-overlay`, which only closes it |

The extension's `metadata.json` says its protocol's version as `thermi-protocol`, and thermi calls only what the running one declares (`src/Thermi.Gnome/README.md`). `thermi-sensors --console --root <tree>` serves a recorded tree in the session, on a socket of the test's own, which `THERMI_SENSORS_SOCKET` names for thermi to read alone; a tree of one second, served every second the same, holds a whole screen still.
