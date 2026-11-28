# 0021. thermi on macOS: IOKit as the person, launchd agents, AppKit through .NET's bindings, and an app that updates itself

**Status**: Accepted

## Context

ADR 0018 leaves room for another OS as two projects beside the shared ones, and ADR 0019 brought Linux that way. The Mac is next: many of the developers thermi is for run their agents on one, and a Mac mini is a common box to leave them running on, often reached only over SSH. Everything thermi does on Windows comes to the Mac but FPS: macOS lets no program count another's frames, as ETW's presents do on Windows (ADR 0014), and Metal's own counter shows only inside the game, when the game turns it on.

Four things differ from Windows and Linux, and they decide the shape:

- **Every sensor reads as the person.** On Apple silicon the SMC (the `AppleSMC` service of IOKit) gives every temperature, fan and voltage it has to any user, the HID event system gives the dies' and the SSD's temperatures, IOReport's `Energy Model` gives the CPU's, the GPU's and the memory's energy, and the GPU's `IOAccelerator` gives its load. Measured on a Mac mini M4 with macOS 26.6, as a plain user over SSH, with no graphical session: 174 temperature keys, the fan's speed, minimum and maximum, the DC input's voltage, 40 HID temperatures, the CPU's energy each second and the GPU's load all read; nothing asked for root, a password or a privacy permission. Only setting a fan's speed needs root, and thermi never sets one. Load, memory, disks, network, battery and uptime are Mach and BSD calls any process makes.
- **launchd starts a person's programs.** A LaunchAgent in `~/Library/LaunchAgents` runs as the person, with no administrator, in their graphical session or in the background session an SSH login opens. launchd can own its socket and start it with the first client. Measured on the same Mac over SSH: an agent loaded with `launchctl bootstrap user/<uid>`, of session type `Background`, was started by its socket at the first connection and served the next one too.
- **Only Apple's frameworks keep a window on top of everything.** A panel of AppKit at a high window level, joining every Space and full-screen apps, that ignores the mouse where it draws nothing and never activates its app, is what menu-bar monitors such as Stats use. There is no other way: no desktop extension as on GNOME, no protocol as layer shell.
- **A Mac's apps update themselves.** macOS updates no third party's app; Chrome, Firefox and the apps on Sparkle each replace their own bundle, which the person who dragged it to Applications owns. A notarized app signed with its maker's Developer ID runs with no warning.

## Decision

Three projects, as ADR 0018 lays out, with `thermi-cli` in a project of its own:

| Project | Is | What it does |
| --- | --- | --- |
| `Thermi.Sensors.MacOS` | `thermi-sensors`, a LaunchAgent of the person's, started by its socket | Reads the SMC, the HID sensors, IOReport, the GPU's statistics and the Mach and BSD counters once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to its socket. It checks for updates and replaces `thermi.app`. Plain `net10.0`, so it builds and replays its recordings on any OS. |
| `Thermi.MacOS` | `thermi` and `thermi settings`, as the person | The overlay: Core decides, CoreGraphics and CoreText paint, an AppKit panel shows. The menu bar's items, the hotkeys and the notices. The settings window: the page in WKWebView. `net10.0-macos`. |
| `Thermi.MacOS.Cli` | `thermi-cli` | Runs `AgentRun` over an `IAgentEnvironment` of the socket, the settings file and the agents' plists, and asks the running overlay its monitors over its socket. Plain `net10.0`, so it never loads AppKit and answers on a box no one ever signed in to graphically. |

- **AppKit through .NET for macOS.** `Thermi.MacOS` targets `net10.0-macos`: Microsoft's bindings, generated from Apple's headers, of AppKit, WebKit, CoreGraphics, CoreText and UserNotifications, as CsWin32 is on Windows and GirCore on Linux. It builds only on a Mac with Xcode, so on Windows and Linux the solution builds it as nothing, and the CI's macOS job builds and tests it. Declared by hand, and listed in the rule with why, are what no binding covers: the SMC's and IOKit's few calls, IOReport and the HID event system, which are libraries Apple ships without headers, Carbon's hot keys, and libSystem's `launch_activate_socket` and `getpeereid`.
- **The sensors read as the person, from one service per person.** `thermi register`, which the app's first start runs, writes two agents into `~/Library/LaunchAgents` and bootstraps them. The service's agent is of session type `Background`, in the person's user domain, so it serves an SSH session and the desktop alike; launchd owns its socket, `~/Library/Application Support/thermi/sensors.sock`, which only the person may open, starts it with the first client and launchd's `StartInterval` every 4 hours so it checks for updates, and it ends 10 s after the last client. A client trusts the socket when `getpeereid` says the person serves it. Nothing of thermi's runs as root.
- **The overlay is an AppKit panel.** One borderless, non-activating `NSPanel` per overlay, at the status window level, joining all Spaces and full-screen apps, off the Dock and ⌘-Tab, whose app is an agent (`LSUIElement`). Clicks pass through where it draws nothing, as the window server does for a transparent window, and everywhere when it is click-through. Full screen comes from the window list's bounds, which need no permission. The pointer's place comes from `NSEvent`'s, and the hotkeys from Carbon's `RegisterEventHotKey`, so thermi asks for no privacy permission but notifications'. The overlay's agent is of session type `Aqua`; launchd starts it at login while Open at login is on, and again when it fails, but not after Exit thermi, which ends it with 0.
- **The menu bar takes the taskbar's place.** thermi's icon and the items' tray icons are status items, `NSStatusItem`, with Core's menus as `NSMenu`. An overlay snaps to the corners of its monitor's visible frame, beside the menu bar and the Dock, but not onto the menu bar: the front app's menus fill it from the left and macOS places the status items from the right, so a window there covers one or the other.
- **The settings window is the same page in WKWebView**, in a process of its own, as ADR 0005 says on Windows. WKWebView speaks WebKit's messages, as WebKitGTK does, so the page and its generated bridge client stay as Linux left them.
- **One app, signed, notarized, and updated by its service.** Each release builds `thermi.app` for Apple silicon, self-contained, signed with thermi's Developer ID and the hardened runtime, notarized and stapled, in a `.dmg`; a Homebrew cask in thermi's tap installs the same app for developers, links `thermi-cli` and runs `thermi register`. The service updates the app as ADR 0008 says on Windows: the same signed manifest, the same checks, then the new app's code signature must be thermi's Developer ID, by the team ID built in, and notarized, before it replaces the person's bundle at a quiet moment and the overlay hands over. The previous app is kept, to go back to when the new one fails.

## Rejected

- **A privileged helper**, a LaunchDaemon as root, for the sensors: nothing it would read needs root, and a root process that parses what it reads is a risk for no gain.
- **Calling Objective-C by hand**, through `objc_msgSend` and classes made at run time: it builds anywhere, but every window, view, menu, web view and delegate becomes code of ours that no compiler checks, for the APIs a binding already covers.
- **A small Swift host** for the windows, speaking to C# over a pipe: a second language and a second process for each overlay, and a protocol to keep in step with Core's.
- **`SMAppService`** to register the agents: it registers only from the app, run in a graphical session, which a Mac reached over SSH never has; `launchctl` does the same with plain plists.
- **A `.pkg`** that installs as root: thermi needs no root, and root's files are ones the person's service cannot update.
- **Intel Macs**, a universal app: macOS 26 is the last release for them, their SMC has other keys, and no Mac to test them on exists.

## Consequences

- Core learns Apple silicon's sensor names, M1 to M4, tested from a recording of the Mac mini M4 and from a virtual machine of the CI's, which has no sensors; the other chips come from hardware reports.
- The CI gets a macOS job, required by `CI gate`: Core's tests, the macOS tests, the page's Playwright tests on WebKit, and the app built, signed ad hoc and run. A release signs and notarizes with the owner's Developer ID, from the CI's secrets.
- The rules that keep tests off the person's desktop hold on the Mac too: an overlay or window test runs only in a session made for tests, the CI runner's or a test Mac's user of its own, and is skipped anywhere else.
- What to watch: IOReport and the HID event system are private, and a macOS release may change them; their readers are small and replayed from recordings, so a change costs one reader. SMC keys change with each chip: Core maps them from a table per chip family.
