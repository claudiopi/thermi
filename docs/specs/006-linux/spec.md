# 006. thermi on Linux

**Status**: approved

**Budget**: 46 .NET and 4 page cases

thermi runs on Linux, all of it but FPS: a service reads the sensors the kernel exposes, the overlay stays above every window on GNOME, KDE Plasma, Hyprland, Sway, Cinnamon, Xfce and MATE, the settings window is the same page as on Windows, `thermi-cli` and its MCP server answer agents as they do there, and updates come through the system's own updater. Many of the developers thermi is for run their agents on Linux, often on a box with no screen, so the sensors and `thermi-cli` install alone too. How it is built, and why each desktop gets its own surface, is in `docs/adr/0019-linux.md`.

## Requirements

### Installing and updating

- R1. One build per release gives two packages, for amd64 and arm64, in three formats: `.deb` for Ubuntu 24.04 and 26.04 and Debian 13, `.rpm` for the Fedora releases supported on its day, and `thermi-bin` on the AUR for Arch. `thermi-sensors`, the sensor service and `thermi-cli`, installs alone on a box with no desktop; `thermi`, the overlay, the settings window and the GNOME Shell extension, needs it. Installing them with the distribution's own tool downloads nothing but its libraries, and removing them stops what they started and keeps each person's settings.
  Release check: Install on Linux.
- R2. `thermi-cli` is on every user's `PATH` and runs from bash, zsh and fish as spec 003 says it runs from PowerShell: the shell waits for it, it prints to that shell and exits with its code.
- R25. The `.deb` and the `.rpm` add thermi's repository, stable by default or beta, signed with a key of thermi's that they install, so each update comes through the system's own updater, as the system's do; the release workflow also updates `thermi-bin` on the AUR. Once a package changed thermi, the service restarts at once, and each overlay and its window hand over to the new version at a quiet moment, as spec 004 says on Windows. About says the version, the channel and that updates come from the system.
  Release check: An update on Linux.
- R26. The owner's levers of spec 004 act on the repositories too: `publish` and `promote` add a version to a channel; `rollback` takes a channel back by publishing the version before under a higher version number, so every PC takes it; `disable` publishes nothing more. A repository that is not signed by thermi's key is refused by the PC's package manager, and an older one never takes a PC back to an older version: apt keeps the newer index it holds, and dnf, which has no such check, finds nothing newer to install.

### Sensors

- R3. While a client listens, the service reads once a second, with no driver to install: the CPU's temperature, load, clock and power; the board's temperatures, fans and voltages, from any chip the kernel has a `hwmon` driver for; NVMe drives' temperatures; an NVIDIA GPU's temperature, load, power, fan and memory through its driver, and an AMD or Intel GPU's from their `hwmon` and `/sys`; the RAM used and in total; each drive's activity and throughput; each physical network adapter's throughput, with loopback, bridges, containers' and VPNs' adapters set apart as virtual; a laptop's battery; and the uptime, the local IP and the clock, as spec 002 reads them from Windows.
  Hardware report: Linux.
- R4. The service runs as a system user of its own with no capability, in a sandbox, and the CPU's energy counters, which the kernel lets only root read, are readable by that user's group and no one else. No process of thermi's runs as root.
- R5. The service is started by its socket when the first client connects and ends 10 s after the last leaves. Any local user may read the socket, and the service never reads from it. A client trusts it only when the process on the other end is root's or the service's user's, and one user holds at most 8 of its 32 connections; a person who may run processes as many users, as one with subordinate uids for containers, can hold them all while their clients stay, a known limit that keeps out no one such a person could not keep out already. Where it is missing or refuses, a client reads the socket `thermi-sensors --console` serves in the person's runtime folder, which only they and root write, and trusts it when they or root serve it; `THERMI_SENSORS_SOCKET` names the one to read in their place, trusted the same. Each line is Core's snapshot line, as on Windows' pipe.
- R6. The same metrics read on Linux as on Windows, from the names the kernel gives: the CPU's temperature from `Tctl` on AMD and `Package id 0` on Intel, its fan from the board's chip as on Windows, the GPU's as on Windows. A value the kernel does not give, as a file missing, unreadable or that fails to read, or a sensor not connected, which some chips report as -40 °C or below, is missing, never a number. With no `hwmon` at all, as in a virtual machine, what the overlay shows reads missing.
- R7. `thermi-sensors --dump <file> [--seconds <n>]` saves snapshots as on Windows, and `--tree <folder>` copies the files under `/sys` and `/proc` that it read, and what NVML answered, so tests replay the reader itself; neither names the PC, a person, an address or a serial number.

### The overlay

- R8. On GNOME Shell 46 to 51, on Wayland and X11, each overlay shows above every window and the top bar, where it was placed, and hides while a window fills its monitor full screen. Clicks pass through it where it draws nothing, and everywhere when it is click-through.
- R17. On KDE Plasma 6, Hyprland and Sway, on Wayland, each overlay shows above every window and the panels, where it was placed, and a full-screen window covers it. Clicks pass through it as on GNOME. On a Wayland desktop with neither GNOME Shell nor layer surfaces, `thermi` shows no overlay and says why once.
- R24. On X11 desktops, as Cinnamon, Xfce, MATE and KDE Plasma's X11 session, each overlay shows above every window and the panels, off the taskbar and the window switcher, and a full-screen window in front covers it. Clicks pass through it as on GNOME.
- R9. When `thermi` starts in a GNOME session that lacks its extension, it asks the Shell to install it from extensions.gnome.org, which GNOME confirms with its own prompt, so the overlay shows without signing out; declined or offline, the copy the package installed shows it from the next sign-in, and a notice says so. With the package's copy in place, that notice, and the window, offer no button. An extension of another protocol version than `thermi`'s shows nothing, and the notice says to update.
  Release check: Install on Linux.
- R10. The overlay shows what it shows on Windows, by Core's layout and looks: every style, level, missing value and "Sensors stopped", sharp at each monitor's scale, fractional ones too. `thermi preview` writes the same picture.
- R11. An overlay is dragged to its place, snaps to a corner of its monitor's work area or onto a panel of it as onto a taskbar, GNOME's top bar among them, anywhere along it, and stays there across restarts and monitor changes, as spec 002 says of the taskbar on Windows.
  Release check: Restart and sign-in on Linux.
- R12. A right-click opens the overlay's menu of Core's entries, on GNOME as the Shell's own popup menu and elsewhere as GTK's. Each entry does what it does on Windows, and Undo takes it back; "Task Manager" opens the desktop's system monitor.
- R23. The desktop gestures of spec 002 work on every surface: a drag moves an overlay and merges it into another; a hold lifts a pill or an item to join it elsewhere or make it an overlay; Esc puts everything back; Ctrl and the wheel resize. Fade near the pointer fades as on Windows on GNOME and X11, and on KDE Plasma, Hyprland and Sway, which tell a program where the pointer is only over its own surfaces, as the pointer comes over the overlay.
- R13. The overlay starts at sign-in, one per person's session, comes back on its own if it fails, and not after "Exit thermi"; starting at sign-in is a switch, as on Windows.
  Release check: Restart and sign-in on Linux.
- R14. At rest the overlay holds 48 MB and 0.5 % of one core, the desktop's compositor no more than 0.5 % of one core beyond what it takes without thermi, the idle window 1 %, and the service 80 MB and 3 %, as `AGENTS.md`'s budgets say.
  Release check: Costs at rest on Linux.

### The settings window

- R18. The settings window of spec 002 runs on Linux, in a process of its own, with the same page and every requirement of spec 002 but those it names Windows' and those R19 gives their Linux form. It opens from the overlay's menu, thermi's icon, a double-click on a value, `thermi settings` and the desktop's app launcher; opened from the overlay, it comes in front of the windows over it, as on Windows; one window exists; and closing it leaves no process behind.
  Release check: Open the window every way on Linux.
- R19. On Linux, the window follows the desktop's light or dark scheme, its accent and high contrast unless Settings chose its own theme; "Start with Windows" reads "Start at sign-in"; the fixes it offers are those of Linux: installing or enabling the GNOME extension, and starting a stopped service through the desktop's own prompt for administrator rights, with no driver to install; and the page reads well and reads aloud with Orca as with Narrator. Its texts are in every translation.
  Release check: Orca, themes and the window's frame on Linux.

### Tray, hotkeys and notices

- R20. thermi's icon and the items' tray icons of spec 002 show on every desktop: in GNOME's top bar through the extension, and elsewhere in the desktop's tray as StatusNotifierItems, with the same menus, values and colors, and kept across restarts.
- R21. The four hotkeys of spec 002 work on every desktop: through the extension on GNOME, through the desktop's GlobalShortcuts portal on KDE Plasma and Hyprland, which asks the person once, and by grabbing the keys on X11. A combination another program holds is refused as on Windows.
- R22. thermi's notices, the first start's, "Notify me when critical" and the extension's, are the desktop's own notifications, and their buttons do what they do on Windows.

### Agents

- R15. Every command and MCP tool of spec 003 answers on Linux with the same JSON and schemas: `status` says whether the service is installed and serving, whether an overlay runs, and on a desktop where the overlay cannot show, that it cannot; `monitors` gives the desktop's monitors with their panels; `start-with-windows` keeps its name, as the contract does, and sets the start at sign-in; `find-cpu-fan` loads the CPU at the scheduler's idle priority. The .NET SDK finds `thermi-cli` on Linux.

### FPS

- R16. A settings file written on Windows works on Linux and loses nothing, and one written on Linux works on Windows. FPS and frame time are not read on Linux: their items take no room in the overlay, the window shows them as missing with why, and `status` says so.

## Design

| Where | What |
| --- | --- |
| `Thermi.Sensors.Linux` | `thermi-sensors`: one reader per source (`hwmon`, NVML, `/proc/stat`, `/proc/meminfo`, `/proc/diskstats`, `/sys/class/net`, `/sys/class/powercap`, `/sys/class/power_supply`), each from a root folder that tests point at a recorded tree, and the socket's server, which writes with Core's `SnapshotWriter`. Sensors keep the kernel's names, a chip's model as its vendor writes it (`nct6798` is `Nuvoton NCT6798`) and an unlabeled input as LibreHardwareMonitor numbers it (`fan2` is `Fan #2`), so Core's rules for a chip hold on both. |
| `Thermi.Linux` | `thermi`, `thermi settings` and `thermi-cli`. The overlay's host: the socket's client, Core's `ThermiMark`, a painter on Cairo and Pango, and three surfaces: the D-Bus link to the extension on GNOME, a GTK 4 layer surface on KDE Plasma, Hyprland and Sway, and a GTK 4 window kept above on X11. The settings window: a GTK 4 window with WebKitGTK that answers the page through `BridgeCalls`, as Windows' does. The tray as StatusNotifierItems, hotkeys through the portal or X11, notices through freedesktop notifications. `thermi-cli` runs `Thermi.Cli`'s `AgentRun` over an `IAgentEnvironment` of the socket, `~/.config/thermi`, the systemd user unit and the desktop's monitors. |
| `Thermi.Gnome` | The extension, in JavaScript: a surface per overlay in the Shell's top layer, thermi's icons in the top bar, the hotkeys, the pointer and the monitors sent back, Core's menus drawn as `PopupMenu`. |
| `src/Thermi.Core` | Linux's sensor names in `SensorMap`, the -40 °C of a sensor not connected, the texts of Linux's own in every translation, and the fixes and menu entries each OS offers. |
| `src/Thermi.App` | What Windows' project held that Linux needs too, as the desktop gestures' logic. |
| `src/Thermi.Settings.Types` | The bridge's client, which speaks WebView2's messages or WebKit's, whichever the page finds. |
| `src/Thermi.Settings.Web` | The page, unchanged but for Linux's texts and fixes, its Playwright tests run on WebKit as well as on Chromium. |
| `tests/Thermi.Tests` | Linux recordings, the trees and their snapshots, from a Ryzen 9 5900X with an RTX 3060 on Ubuntu 26.04, as the service's user and as a plain user, and from a virtual machine with no `hwmon`. |
| `Thermi.Linux.Tests` | The reader from recorded trees, the socket, the CLI on Linux, the window and the overlay against a headless GNOME Shell, KWin, Sway and an X11 desktop of the test's own, each started with its own D-Bus session, driven through the compositor's own input for tests and read back through its screenshots: the rule that no test touches the person's desktop holds on Linux too. |
| `.github/workflows/ci.yml` | Jobs on Ubuntu, required by `CI gate`: Core's tests and the Linux tests, the extension's lint, the page on WebKit, and the packages built for both architectures and installed in clean containers of each distribution, from a repository the levers wrote. |
| `docs/RELEASES.md` | The Linux checks, run on the physical Ubuntu PC and on the test machines, one per desktop and package. |

Logs go to `~/.local/state/thermi`, and the service's to the journal. The settings file is the same file, under `~/.config/thermi`, under the same mutex.

**Rejected:** FPS on Linux, from a Vulkan layer loaded into each game, as MangoHud does: it reaches only the games that load it, and few people play on Linux; spec 005's items already take no room where frames are not read.

## Acceptance

The steps are in `acceptance.json`; the work is in `tasks.md`.
