# Thermi.MacOS

`thermi` on macOS, as the person (`docs/adr/0021-macos.md`). This build shows nothing yet: it registers its agents, or starts AppKit and ends. What it is to hold, the overlay's AppKit panels, the menu bar's items, the settings window in WKWebView and the person's two LaunchAgents, is in `docs/specs/008-macos/spec.md`.

| Folder | What lives there |
| --- | --- |
| `Agents` | `thermi register` and `thermi unregister`, which load the person's two LaunchAgents into launchd, or take them out, before AppKit starts. |

It targets `net10.0-macos`, .NET for macOS's bindings of Apple's frameworks, which build only on a Mac with Xcode: on Windows and Linux the solution builds it as a project of nothing. Its tests, in a session made for tests, are in `tests/Thermi.MacOS.Tests`.
