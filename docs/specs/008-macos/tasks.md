# 008-macos: Tasks

**Status**: approved; 8 of 27 tasks; current block B3.

## B1. The ground

- [x] T1. ADR 0021 accepted, and the rules of `AGENTS.md` that name Windows extended to macOS: a `*.MacOS` project's files use a macOS-only API, the native calls declared by hand are listed with why, macOS's window tests run only in a session made for tests. (R9, R16)
- [x] T2. The macOS job in `.github/workflows/ci.yml`, required by `CI gate`, running Core's tests on macOS first, and the solution building `Thermi.MacOS` as nothing off macOS. (R1)
- [x] T3. The acceptance tests, written red and skipped. (all)

## B2. The sensors

- [x] T4. The recordings of the Mac mini M4 with `--raw`, at rest and under load, and of the CI's virtual machine, and their snapshots. (R8)
- [x] T5. `Thermi.Sensors.MacOS`'s readers over a recorded source: the SMC, the HID sensors, IOReport, the GPU, the Mach counters, drives, adapters, the battery, the uptime, local IP and clock. (R5)
- [x] T6. Apple silicon's names in Core's `SensorMap`, by chip family, M1 to M4, from Stats' keys and the M4's recording. (R6)
- [x] T7. The socket's server and client under launchd, with the peer check. (R7)
- [x] T8. `thermi register` and `thermi unregister`, and the agents' plists. (R1, R7, R14)

## B3. thermi-cli

- [ ] T9. `Thermi.MacOS.Cli`'s `IAgentEnvironment` and `thermi-cli`, the SDK finding it, and `find-cpu-fan`'s load on the performance cores. (R2, R22)
- [ ] T10. A settings file from Windows and Linux, and FPS missing on macOS. (R23)

## B4. The overlay

- [ ] T11. The painter on CoreGraphics and CoreText, and `thermi preview`. (R10)
- [ ] T12. The session made for tests: on the CI's runner and the test Mac's user, the screen read back through its capture. (R9)
- [ ] T13. The `NSPanel` per overlay: its level, Spaces, full screen, clicks through and no focus. (R9)
- [ ] T14. Dragging and snaps to the visible frame, the Dock's moves, and monitor changes. (R11)
- [ ] T15. The desktop gestures, ⌘ and the wheel, a pinch, and Fade near the pointer. (R13)
- [ ] T16. The menu, and the overlay's agent: Open at login, its restart and Exit thermi. (R12, R14)

## B5. The settings window

- [ ] T17. The window's process with WKWebView, opened every way, with its previews painted by CoreGraphics, and the page's Playwright tests on WebKit in the macOS job. (R17)
- [ ] T18. The Mac's appearance, accent and contrast, Open at login, and macOS's fixes, in every translation. (R18)

## B6. Menu bar, hotkeys and notices

- [ ] T19. thermi's icon and the items' tray icons as menu bar items, with their menus. (R19)
- [ ] T20. The hotkeys through Carbon's hot keys. (R20)
- [ ] T21. The notices through UserNotifications. (R21)

## B7. Packing and updates

- [ ] T22. The app bundle, signed with the hardened runtime, the `.dmg` and the Homebrew cask, built and opened in the CI. (R1, R16)
- [ ] T23. The release workflow's signing and notarizing with the owner's Developer ID, and the tap's update. (R1, R4)
- [ ] T24. The service's updater: the manifest's Mac app, the code signature's team ID and notarization, the swap and going back. (R3)
- [ ] T25. The hand-over to a new version and About on macOS. (R3)
- [ ] T26. The levers on the Mac's app. (R4)
- [ ] T27. The macOS release checks run on the test Mac, with real captures of the overlay, the macOS tests among them, whose Retina and two-monitor rows pass there alone. (R1, R3, R10, R11, R14, R15, R17, R18, R22)
