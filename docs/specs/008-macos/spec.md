# 008. thermi on macOS

**Status**: approved

**Budget**: 44 .NET and 1 page cases

thermi runs on Macs with Apple silicon, all of it but FPS: a service reads the sensors as the person, with no driver, no root and no privacy permission, the overlay stays above every window, on every Space and over full-screen apps, the menu bar takes the taskbar's place, the settings window is the same page as on Windows, `thermi-cli` and its MCP server answer agents as they do there, and the app updates itself as Chrome does. Many of the developers thermi is for leave their agents running on a Mac, often a Mac mini reached only over SSH, so the sensors and `thermi-cli` work there with no one signed in at its screen. How it is built, and why nothing of it runs as root, is in `docs/adr/0021-macos.md`.

## Requirements

### Installing and updating

- R1. One build per release gives `thermi.dmg`, which holds `thermi.app` for Apple silicon on macOS 15 and 26, signed with thermi's Developer ID and the hardened runtime, notarized and stapled, and a cask in thermi's Homebrew tap that installs the same app. The app opens with no warning from Gatekeeper. Its first start, or `thermi register`, which the cask runs, puts thermi's two LaunchAgents in the person's `~/Library/LaunchAgents` and starts them, with no administrator prompt; `thermi unregister`, which the cask's uninstall runs, stops and removes them, and keeps the person's settings.
  Release check: Install on macOS.
- R2. `thermi-cli` runs from zsh and bash as spec 003 says it runs from PowerShell: the shell waits for it, it prints to that shell and exits with its code. The cask links it into Homebrew's `bin`; from the `.dmg`, the window's fix links it into `/usr/local/bin` through macOS's own prompt for an administrator.
- R3. The service checks for updates as spec 004 says, from the same channels, whose manifests list the Mac's app beside Windows' zips: a minute after it starts and every 4 hours, as launchd starts it to check even when no client listens. It takes a version only when the manifest's trust of spec 004 holds and the new app's code signature is thermi's Developer ID, by the team ID built into thermi, and notarized. It replaces the person's `thermi.app` whole or not at all, keeps the previous one, and goes back to it when the new one fails, as spec 004 says. Each overlay and its window hand over to the new version at a quiet moment. A person who cannot write the app, as when another account installed it, gets no update, and About says why.
  Release check: An update on macOS.
- R4. The owner's levers of spec 004 act on the Mac's app too: the release workflow publishes its `.dmg` and its zip, signed and notarized with the owner's Developer ID from the CI's secrets, and updates the Homebrew tap's cask; `publish`, `promote`, `rollback` and `disable` act on it as on Windows'.

### Sensors

- R5. While a client listens, the service reads once a second, as the person: the CPU's temperature, load, clock and power, for each cluster of cores too; the GPU's load, power and memory in use, and its temperature where the chip gives one; the SoC's and the SSD's other temperatures; the fans' speeds, where the Mac has fans; the voltages the SMC gives, as the DC input's; the RAM used and in total; each drive's activity and throughput; each physical network adapter's throughput, with loopback, tunnels, bridges and Apple's peer-to-peer adapters set apart as virtual; a MacBook's battery; and the uptime, the local IP and the clock, as spec 002 reads them from Windows.
  Hardware report: macOS.
- R6. The same metrics read on macOS as on Windows, from the names Apple gives, by a table per chip family, M1 to M4: the CPU's temperature is its cores' hottest, its power IOReport's CPU energy each second. A value the Mac does not give, as a fan on a MacBook Air or a key its chip lacks, is missing, never a number. In a virtual machine, which has no SMC, what the overlay shows reads missing.
- R7. The service is a LaunchAgent of the person's, in their background session, so one serves their desktop and their SSH sessions alike. launchd starts it with the first client of its socket and it ends 10 s after the last leaves. Only the person may open the socket, and the service never reads from it. A client trusts it only when the process on the other end is the person's; `THERMI_SENSORS_SOCKET` names another to read, trusted the same. Each line is Core's snapshot line, as on Windows' pipe.
- R8. `thermi-sensors --dump <file> [--seconds <n>]` saves snapshots as on Windows, and `--raw <folder>` records what the SMC, the HID sensors, IOReport and IOKit's registry answered, so tests replay the reader itself; neither names the Mac, a person, an address or a serial number.

### The overlay

- R9. Each overlay shows above every window and the Dock, where it was placed, on every Space, and above an app full screen as its "When to show" says, as on Windows: shown by default, hidden with "Hide in full screen". Clicks pass through it where it draws nothing, and everywhere when it is click-through. It never takes the focus from the app in front, and shows in neither the Dock nor ⌘-Tab.
- R10. The overlay shows what it shows on Windows, by Core's layout and looks: every style, level, missing value and "Sensors stopped", sharp on a Retina monitor and on one that is not. `thermi preview` writes the same picture.
  Exception: With no driver to install on macOS, a value never reads "Driver" in place of "–".
- R11. An overlay is dragged to its place, snaps to a corner of its monitor's visible frame, beside the menu bar and the Dock, and stays there across restarts and monitor changes, and when the Dock moves or hides.
  Release check: Sign-in on macOS.
  Exception: No overlay snaps onto the menu bar: the front app's menus fill it from the left and macOS places the status items from the right, so a window there covers one or the other. The values go there as menu bar items instead (R19).
- R12. A right-click or a Control-click opens the overlay's menu of Core's entries, as a menu of macOS's own. Each entry does what it does on Windows, and Undo takes it back; "Task Manager" opens Activity Monitor.
- R13. The desktop gestures of spec 002 work: a drag moves an overlay and merges it into another; a hold lifts a pill or an item to join it elsewhere or make it an overlay; Esc puts everything back; ⌘ and the wheel, or a pinch on a trackpad, resize, as Ctrl and the wheel do on Windows. Fade near the pointer fades as on Windows.
- R14. The overlay starts at login, one per person's session, comes back on its own if it fails, and not after "Exit thermi"; starting at login is a switch, as on Windows.
  Release check: Sign-in on macOS.
- R15. At rest the overlay holds 48 MB and 0.5 % of one core, the window server no more than 0.5 % of one core beyond what it takes without thermi, the idle window 1 %, and the service 80 MB and 3 %, as `AGENTS.md`'s budgets say.
  Release check: Costs at rest on macOS.
- R16. thermi asks for no privacy permission but to show notifications: no screen recording, accessibility, input monitoring or local network.

### The settings window

- R17. The settings window of spec 002 runs on macOS, in a process of its own, with the same page and every requirement of spec 002 but those it names Windows' and those R18 gives their macOS form. It opens from the overlay's menu, thermi's menu bar item, a double-click on a value, `thermi settings`, and opening `thermi.app` from the Finder, Launchpad or Spotlight; it comes in front, focused; one window exists; and closing it leaves no process behind.
  Release check: Open the window every way on macOS.
- R18. On macOS, the window follows the Mac's light or dark appearance and its accent unless Settings chose its own theme, and Increase contrast even then, as high contrast on Windows; "Start with Windows" reads "Open at login"; the fixes it offers are those of macOS: starting the stopped service again, with no prompt, and adding `thermi-cli` to the `PATH`, with no driver to install; and the page reads well and reads aloud with VoiceOver as with Narrator. Its texts are in every translation.
  Release check: VoiceOver, themes and the window's frame on macOS.
  Exception: With no driver, a double-click on a value opens no driver bar.

### Menu bar, hotkeys and notices

- R19. thermi's icon and the items' tray icons of spec 002 show in the menu bar as its items, with the same menus, values and colors, and are kept across restarts.
  Exception: macOS hides the menu bar items that do not fit, beside a MacBook's notch too, and says nothing to the app; an item it hides shows again when room comes back.
- R20. The four hotkeys of spec 002 work, with no permission asked. A combination another program holds is refused as on Windows.
- R21. thermi's notices, the first start's and "Notify me when critical", are macOS's own notifications, from thermi's name and icon, and their buttons do what they do on Windows. macOS asks the person once whether thermi may show them.

### Agents

- R22. Every command and MCP tool of spec 003 answers on macOS with the same JSON and schemas: `status` says whether the service is installed and serving and whether an overlay runs; `monitors` gives the Mac's monitors with their visible frames, as the running overlay places its overlays on them, and with none running says so, so `thermi-cli` needs no window server; `start-with-windows` keeps its name, as the contract does, and sets Open at login; `find-cpu-fan` loads every core at the lowest priority that still reaches the performance cores, as macOS keeps a background thread on the efficiency cores. The .NET SDK finds `thermi-cli` on macOS.
  Exception: The service is started by its socket (R7), so a `thermi-cli` read starts it, as any client's does, even one a person stopped, where spec 003's R4 says nothing `thermi-cli` does starts it; `thermi-cli` still starts nothing itself.

### FPS

- R23. A settings file written on Windows or Linux works on macOS and loses nothing, and one written on macOS works on both. FPS and frame time are not read on macOS: their items take no room in the overlay, the window shows them as missing with why, and `status` says so.
  Exception: With no FPS, nothing follows the game in front, and no window of thermi's needs keeping out of its count.

## Design

| Where | What |
| --- | --- |
| `Thermi.Sensors.MacOS` | `thermi-sensors`, plain `net10.0`: one reader per source (the SMC, the HID sensors, IOReport, the GPU's `IOAccelerator`, the Mach counters, the drives' and the adapters' statistics, the battery), each over a source that tests point at a recording, and the socket's server, which writes with Core's `SnapshotWriter`. The updater: it checks, verifies the app's signature and swaps the bundle. |
| `Thermi.MacOS` | `thermi`, `thermi settings`, `thermi register` and `thermi unregister`, on `net10.0-macos`. The overlay's host: the socket's client, Core's `ThermiMark`, a painter on CoreGraphics and CoreText, and an `NSPanel` per overlay. The settings window: an `NSWindow` with WKWebView that answers the page through `BridgeCalls`, as Windows' does. The menu bar's items, Carbon's hot keys, UserNotifications' notices, and the agents' plists. |
| `Thermi.MacOS.Cli` | `thermi-cli`, plain `net10.0`: `Thermi.Cli`'s `AgentRun` over an `IAgentEnvironment` of the socket, `~/Library/Application Support/thermi`, the overlay's agent and the monitors the running overlay tells. |
| `src/Thermi.Core` | Apple silicon's sensor names in `SensorMap`, by chip family, the texts of macOS's own in every translation, and the fixes and menu entries macOS offers. |
| `src/Thermi.Settings.Web` | The page, unchanged but for macOS's texts and fixes; its Playwright tests already run on WebKit. |
| `tests/Thermi.Tests` | Mac recordings and their snapshots, from a Mac mini M4 on macOS 26, and from a virtual machine of the CI's, which has no SMC. |
| `Thermi.MacOS.Tests` | The reader from recordings, the socket and the agents' plists, the CLI on macOS, and the window and the overlay in a session made for tests, read back through the screen's capture: on the CI's macOS runner, or a test Mac's user of its own; anywhere else they skip, so no test touches the person's desktop. |
| `.github/workflows/ci.yml` | A macOS job, required by `CI gate`: Core's tests and the macOS tests, the page on WebKit, and the app built, signed ad hoc, packed and opened. |
| `docs/RELEASES.md` | The macOS checks, run on the test Mac, a Mac mini M4 on macOS 26, signed in as a user of its own for tests. |

Logs go to `~/Library/Logs/thermi`. The settings file is the same file, under `~/Library/Application Support/thermi`, under the same lock.

**Rejected:** Intel Macs, with a universal app: macOS 26 is the last release for them, their SMC has other keys, and no Mac to test them on exists.

## Acceptance

The steps are in `acceptance.json`; the work is in `tasks.md`.
