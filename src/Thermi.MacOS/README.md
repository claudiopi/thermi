# Thermi.MacOS

`thermi` on macOS, as the person (`docs/adr/0021-macos.md`). The overlay reads the service's socket, lets Core decide what each overlay shows, paints it with CoreGraphics and CoreText, and shows it in an AppKit panel per overlay, above every window, on every Space and over full-screen apps. thermi's icon and the items' tray icons are menu bar items. `thermi settings` hosts the page of `src/Thermi.Settings.Web` in WKWebView, behind the same bridge as on Windows. `thermi register` and `thermi unregister` write and remove the person's two LaunchAgents.

It targets `net10.0-macos`, .NET for macOS's bindings of Apple's frameworks, which build only on a Mac with Xcode: on Windows and Linux the solution builds it as a project of nothing. Its tests, in a session made for tests, are in `tests/Thermi.MacOS.Tests`.
