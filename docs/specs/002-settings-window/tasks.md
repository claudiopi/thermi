# 002-settings-window: Tasks

**Status**: built in v1; how it was built is in git's history.

## Open

Checks on hardware other than the development PC: [Hardware reports](https://github.com/claudiopi/thermi/issues/HARDWARE_REPORTS) (A218, A235, A239, A241, A252).

By hand on the development PC, before publishing:

- **Open the window every way**: from the overlay's menu, thermi's menu, a double-click on a value and on the edge, `thermi settings` and Start, with the window closed and then behind others; one window opens on the right item or overlay, or comes to the front focused (A215).
- **Measure the window's costs**: with `--trace` and `scripts/measure.ps1 -Window` at 100 % and 150 %, the first frame cold and warm, a knob dragged for a minute, the idle window with the overlay page's preview, and an hour of use; every budget holds and nothing is left once the window closes (A216, A264, A330).
- **Recover from real failures**: the window opened without the overlay, the service stopped and started, the driver's prompt refused and then accepted, the file edited by hand, broken and restored; the window follows each and recovers on its own (A217).
- **Keep a snap across a restart and a port change**: an overlay snapped to the second monitor finds it again (A219).
- **An accessibility pass**: Narrator, light, dark, another accent, high contrast and animation effects off, the window resized, maximized, snapped and activated over the overlays, and the search's marks in both themes (A220, A291).
- **Start with Windows across a reinstall**: turned off, it stays off after a reinstall and a sign-in, and Ctrl+Z never touches it (A221).
- **The overlay reads well everywhere**: light and dark wallpapers and taskbars, every theme, font, effect and 0 % background, each level, the first critical's glow, a stopped service and its tooltips; then 400 % with 36 px text and a pill color on a 1080p monitor at 100 % and a 4K one at 150 %, sharp, whole and within budget (A222, A358).
- **Visibility, hotkeys and menus**: a full-screen video and a borderless game, the four hotkeys, both menus by mouse and keyboard, and every entry of the overlay's menu by mouse and by its key, in English and Spanish, light, dark and high contrast (A223, A327).
- **Spanish and the pseudo-language**: with Windows in Spanish, then `thermi settings --pseudo`, every text thermi writes is Spanish and long texts fit (A224).
- **Tray icons**: thermi's icon and three item icons at 100 %, 150 % and 200 % on dark and light taskbars, across a restart, an Explorer restart and level changes (A225).
- **Windows notices**: the first start, overlays hidden from thermi's menu, click-through, and Notify me (A226).
- **Desktop gestures with four overlays on two monitors**: clicks with the window open, holds that lift pills and items, drops on overlays and the desktop, Esc mid-drag, merges, resizes, a crowded taskbar, and the overlay menu's Undo (A227, A304, A351).
- **Network and disk on a PC with two drives and two adapters**: a large download and a copy between drives; the NET pill and the disk items follow them, and the service stays under 80 MB of private memory at rest (A233).
- **Physical adapters and CPU load against Task Manager**: with WSL or Hyper-V and a VPN on, Auto follows the physical adapter; CPU load stays within 3 points of Windows 11 Task Manager's at rest and under `thermi cpu-load 30` (A240). No PDH sensor is needed: Task Manager shows processor time since 24H2, as CPU Total is; at rest on build 26300, thermi read 11.13 % and Windows' `% Processor Time` 10.95 %.
- **The overlay's additions**: Task Manager from both menus, the models, busiest process and throttling in the tooltips, Copy as image, Fade near the pointer, and Driver once the driver is removed, within budget (A257).
- **A crash and a hang restart the overlay**: Windows offers to restart it and one overlay comes back; not within its first minute, nor after Task Manager or "Exit thermi" ended it (A356).
