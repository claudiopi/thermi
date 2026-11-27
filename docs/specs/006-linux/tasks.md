# 006-linux: Tasks

**Status**: approved; 10 of 30 tasks; current block B4.

## B1. The ground

- [x] T1. ADR 0019 accepted, and the rules of `AGENTS.md` that name Windows extended to Linux: a `*.Linux` project's files use a Linux-only API, the native calls declared by hand are listed with why, Linux's tests stay off the person's desktop. (R4, R8)
- [x] T2. The Ubuntu jobs in `.github/workflows/ci.yml`, required by `CI gate`, running Core's tests on Linux first. (R1)
- [x] T3. The acceptance tests, written red and skipped. (all)

## B2. The sensors

- [x] T4. The trees of the 5900X desktop recorded with `--tree`, as the service's user and as a plain user, and of a virtual machine, and their snapshots. (R7)
- [x] T5. `Thermi.Sensors.Linux`'s readers over a root folder: `hwmon`, `/proc`, `/sys`, the energy counters, the uptime, local IP and clock. (R3)
- [x] T6. NVML, loaded only when present. (R3)
- [x] T7. Linux's names and the -40 °C rule in Core's `SensorMap`. (R6)
- [x] T8. The socket's server and client, with the peer check and the share of connections. (R5)
- [x] T9. The systemd units, the udev rule and the sandbox. (R4, R5)

## B3. thermi-cli

- [x] T10. `Thermi.Linux`'s `IAgentEnvironment` and `thermi-cli`. (R2, R15)
- [ ] T11. The SDK finding `thermi-cli` on Linux, and `status` on frames and desktops. (R15, R16) Done but for A9, which waits for B4: it starts the overlay's unit, `thermi.service`, which B4 packages.

## B4. The overlay

- [x] T12. The painter on Cairo and Pango, and `thermi preview`. (R10)
- [ ] T13. The headless GNOME Shell, KWin, Sway and X11 desktop for tests, in the CI's jobs. (R8, R17, R24)
- [x] T14. The extension and its D-Bus protocol: the picture, the pointer, the monitors, full screen. (R8)
- [ ] T15. The GTK 4 layer surface for KDE Plasma, Hyprland and Sway, its work-area probe, and the Wayland desktops with neither. (R17)
- [ ] T16. The GTK 4 window kept above on X11, its input shape and the panels' struts. (R24)
- [ ] T17. Dragging, snaps, the top bar and the panels. (R11)
- [ ] T18. The desktop gestures moved to `src/Thermi.App`, on every surface, and Fade near the pointer. (R23)
- [x] T19. The menu, the user unit and its restart. (R12, R13)
- [x] T20. The extension from extensions.gnome.org, and its version. (R9)

## B5. The settings window

- [x] T21. The bridge's client over WebKit's messages, and the page's Playwright tests on WebKit. (R18)
- [x] T22. The window's process on GTK 4 and WebKitGTK, opened every way, with its previews painted by Cairo. (R18)
- [x] T23. The desktop's scheme, accent and contrast, Start at sign-in, and Linux's fixes through polkit and the Shell, in every translation. (R19)

## B6. Tray, hotkeys and notices

- [ ] T24. thermi's icon and the items' tray icons: in the extension, and as StatusNotifierItems with their menus. (R20)
- [ ] T25. The hotkeys: the extension, the GlobalShortcuts portal and X11's grab. (R21)
- [ ] T26. The notices as freedesktop notifications. (R22)

## B7. Packages and updates

- [x] T27. One build packed by nfpm as `.deb` and `.rpm`, and the AUR's `thermi-bin`, for both architectures, installed in clean containers of each distribution. (R1)
- [x] T28. thermi's signed apt and dnf repositories, the levers on them and the release workflow's publishing, with the AUR's. (R25, R26)
- [ ] T29. The hand-over to a new version and About on Linux. (R25)
- [ ] T30. The Linux release checks run on the test machines and the physical Ubuntu PC. (R1, R9, R11, R13, R14, R18, R19, R25)
