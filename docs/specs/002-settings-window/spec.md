# 002. A settings window where every change shows on the real overlay

**Status**: draft

## Why

Today thermi is configured by editing JSON: picking the fan that cools the CPU means knowing sensor ids, and thresholds are guesses made without seeing the values they guard. Version 0.5 makes it visual, live and impossible to break, without making the overlay any heavier.

The window follows four rules:

1. **The overlay is the preview.** Every change shows on the real overlay at once, which shows while the window is open. There is no Save button: undo replaces confirmation.
2. **Real data everywhere.** Every choice shows its live value: each sensor in the catalog, each threshold over the values it would have colored.
3. **Nothing can break it.** The window only writes valid settings, hand edits keep working, and a broken file is explained and kept.
4. **Free when closed.** The window lives in its own process; the overlay pays nothing for it.

The mockups are in `docs/specs/002-settings-window/mockups`: `settings.png` is the window, `catalog.png` is the sensor catalog with the fan finder running, `style.png` is the Style tab, and `general.png` the General tab.

Simple first, deep on demand: a preset and a drag cover most people; every size, weight and color is one "Customize" away for those who want it.

Spec 001 is done before this one starts: its by-hand steps use the "Edit settings" item that R1 replaces.

## Requirements

Opening

- R1. "Settings…" in the overlay's menu opens the window. It replaces "Edit settings"; opening the file moves into the window.
- R2. A double-click on the overlay opens the window.
- R3. `thermi settings` opens the window.
- R4. Only one window exists: asking again brings it to the front.
- R5. The first frame shows within 1 s of the request when the window was opened before in this sign-in, and within 2 s the first time.
- R6. Without the overlay running, the window still works, and offers to start it.

Live editing

- R7. A change shows on the real overlay within 300 ms of the input, also while a slider moves. There is no Save button.
- R8. Ctrl+Z and Ctrl+Y, and the buttons in the title bar, undo and redo the window's changes since it opened. One gesture is one step: a drag from press to release, or typing until the field loses focus.
- R9. Undo never reverts a change made outside the window, a hand edit or a drag of the overlay, even of the same item: a step whose target no longer holds what the window left there is skipped, and the window says so.

The stage

- R10. The stage shows the overlay as it is now, enlarged, with live values.
- R11. On the stage, an item is selected with a click, moved with a drag or Alt+arrows, removed with Delete, and added with "+".
- R12. A double-click on a value of the stage opens the catalog to change its sensor.

The catalog

- R13. The catalog lists every sensor the service reads, one row each, grouped by device, with its live value.
- R14. Typing filters it by sensor, device or type, and chips filter it by type.
- R15. Each sensor shows a sparkline of its values since the window opened, up to the last minute.
- R16. A sensor without a usable value says why. No value: "reads nothing". A CPU temperature, power or clock of exactly zero: "reads nothing: the PawnIO driver may be missing". A fan at 0 RPM: "stopped or not connected". The overlay uses the same rule, so only those CPU sensors treat zero as missing.
- R17. Any sensor can be added as an item, shown in the unit of its type, as its own pill. Its label is the sensor's name in capitals, cut to 8 characters; when the item before it has that same label, it gets the first number not used in that run, the name cut so that both fit in 8 characters. The label can be changed.
- R18. An item's sensor can only be changed to another of the same type, or back to "Auto" if the item started from a metric; its meter then goes back to Auto too. When a fan item metered by a fan's duty changes its fan, by hand or by the finder, the meter becomes the new fan's own duty, or shows as missing if that fan has no duty sensor.

Finding the CPU fan

- R19. "Find my CPU fan" first waits for 3 s of fresh readings, then loads every logical core at below-normal priority, for at least 8 s and at most 30 s. Once past 8 s, it stops when one fan has met the rule of R21 for 3 s in a row. A gap in the readings during the run stops it, and it says so.
- R20. It can be cancelled at any time, and the load stops within a second.
- R21. It picks the fan whose speed, averaged over its last 3 s, rose by at least 200 RPM and 20 % over its average in the 3 s before the load, and by at least twice as many RPM as any other fan. The first item showing the CPU fan then names that fan, with its own duty as the meter; if there is none, a fan item is added at the start.
- R22. Otherwise it picks nothing and says why: no fan rose, two rose alike, or the CPU load, averaged after its first 2 s, stayed under 80 %.
- R23. With no fan sensor outside the GPUs, it cannot start. It says the driver may be missing when the CPU temperature also reads zero, and that the board reports no fans otherwise.

An item

- R24. Thresholds are set on a track from 0 to 100 that shows the green, yellow and red zones and the live value of the item's meter, which is what the thresholds color.
- R25. Behind the track, a histogram shows how long the meter's value spent at each level since the window opened, up to ten minutes. Gaps (the service stopped, or its data went stale) stay empty, never filled with the last value.
- R26. The warning value can never be above the critical one.
- R27. A threshold can go back to its default, which follows the type of the meter it colors: 70 and 85 for a temperature, 60 and 80 for a load, 60 and 85 for a duty (so the fan item, metered by its duty, gets 60 and 85).
- R28. An item's meter can be off, or another sensor or metric of a 0 to 100 type (load, duty, temperature in °C). Items of those types can also meter themselves. An item without a meter has no threshold and is never colored.
- R29. A pill's label can be changed, and the change applies to every item in that pill and to no other.

Placement

- R30. The overlay can snap to the left, center or right of the taskbar of any monitor that has one. Left starts a margin in from the taskbar's left end; right ends a margin before the notification area or, on other monitors, the clock; center is the middle of the taskbar and may cover centered app icons, as the map shows. Where the notification area or clock cannot be found from its window, the right spot keeps a fixed reserve of 220 px times the monitor's scale, as today.
- R31. The overlay can snap to any corner of any monitor's work area.
- R32. A snapped overlay keeps its snapped edges in place when its width, its height or the taskbar changes, and the computed position is never written to the file.
- R33. Moving a snapped overlay by more than the system's drag distance unsnaps it, and it stays where it was dropped. A click or a double-click keeps the snap.
- R34. A snap to a monitor that is not connected falls back to the same spot on the primary monitor, and goes back when the monitor returns.
- R35. A monitor is known by the maker, model and serial in its EDID, which survive restarts and a change of port; a monitor without a serial is known by its connection.
- R36. The size is a factor of 75 % to 200 % over the monitor's own scale, 100 % by default.

The settings file

- R37. The window never writes invalid settings.
- R38. A write never loses a change the other process made meanwhile.
- R39. A read that finds the file locked, empty, cut short or missing is retried before it counts, so an editor's save in progress never shows as defaults or as a broken file, and a missing file is recreated only if it is still missing after the retry.
- R40. While the file is broken by hand, the window shows the line and the reason, offers "Open in editor", and allows no other change.
- R41. It also offers "Restore last good settings", which first keeps the broken file beside it under a dated name, then writes the last settings read well in this session, or the defaults if there were none.
- R42. A change made by hand while the window is open shows in it at once, and the window's own writes never come back to it as outside changes.
- R43. The file moves to version 2, and version 1 files are migrated when read.
- R44. Every item has a unique id that every read of the same file gives back the same, including for items added by hand without one or with a repeated one.

The rest

- R45. When the sensor service is not running, the window says so in plain words, says what to do, and recovers on its own when it starts.
- R46. When the service's data goes stale, the window says so and stops showing live values until fresh data arrives.
- R47. Everything can be done with the keyboard alone.
- R48. Narrator reads a name and role for every control.
- R49. The window follows Windows' light or dark mode, accent color and high contrast.
- R50. With animation effects off in Windows, nothing animates.
- R51. While the window is open and idle, the overlay stays within spec 001's budget (under 0.5 % of one core and 40 MB of private memory). While a slider moves, the overlay repaints at most 10 times a second, its own once-a-second refresh included, and stays under 2 % of one core. Closing the window leaves no process behind.
- R52. A "Start with Windows" switch applies at once and survives a reinstall. It changes Windows, not the settings, so Ctrl+Z does not touch it.
- R53. "Reset to defaults" brings back the default items, labels, meters, thresholds, placement, size and style; the language, the visibility, the hotkeys and where thermi shows (R76) stay. Undoing it restores every item and field that still holds its default.

Pills

- R54. A pill is a run of items with the same label. Any item can start a new pill instead, by dragging it out of its pill on the stage or with a switch in the inspector: it and the items after it with the same label form a pill of their own, which shows its label.
- R55. A layout setting shows the pills of R54 (the default), one pill per item, each with its label, or all the items in one bar, with a label where each run of the same label starts. The last two ignore which items start a pill, and keep it for when the layout comes back.

Style

- R56. The overlay can be horizontal (the default) or vertical, where the pills stack from top to bottom, all as wide as the widest. While vertical, the taskbar spots of R30 are not offered, and a taskbar snap shows at the bottom corner of the work area on its side, center going right.
- R57. The presets Compact, Standard (the default) and Large set the text sizes and weights of R58; Standard is today's look. A "Customize" button shows those controls, and changing one makes the style custom, keeping the rest. Opacity, pills and colors are always shown, and never change the preset.
- R58. Labels and values each have a text size, from 9 to 20 px at 100 % in steps of 0.5, and a weight: regular, semibold or bold.
- R59. The pills' background has an opacity from 0 % to 100 %, 100 % by default; below 30 %, the text gets a shadow so it stays readable on any wallpaper. The whole overlay, text and meters included, has its own opacity from 20 % to 100 %, 100 % by default, so it can fade into the screen without ever vanishing.
- R60. The pills can be dark (the default), light, or follow Windows' taskbar mode, the one the overlay sits on, within a second of it changing.
- R61. The meter's green, yellow and red can each be changed, and go back to their defaults.
- R62. Labels and values each have a text color, Auto by default: today's colors on dark pills, and dark ones on light pills. A color with a contrast under 3:1 against its pill's inner color, taken as opaque, is drawn in its Auto color instead, and the window says so, so text never disappears when the pills change.

Your own styles

- R63. A style is the look alone: layout, orientation, sizes and weights, opacity, pills and colors. It remembers the name of the saved style it came from until anything else changes the look.
- R64. The current style can be saved under a name of 1 to 40 characters, unique whatever the case. Saved styles sit next to the presets, and can be renamed and deleted; renaming the chosen one renames it in the settings too. Saving, renaming and deleting change the saved styles, not the look, so Ctrl+Z does not touch them; choosing a saved style is one step that Ctrl+Z undoes.
- R65. A style exports to a `.thermi-style` file that holds the style and its name, and no sensors, items, placement, language or paths, so it works on any PC.
- R66. A style file dropped on the window, or its text pasted with the Import button, or with Ctrl+V when no text field has the focus, applies to the overlay at once, as one step that Ctrl+Z undoes, like any change. A bar then offers Undo and "Save as my style", which saves it under its name, or asks for another when that name is taken.
- R67. A style file that is broken, from a newer thermi, or with a value out of its range is refused whole with the reason, naming the field, and nothing changes. Unknown fields are ignored, and missing ones take their defaults.

Visibility and hotkeys

- R68. A visibility setting shows the overlay always (the default), hides it while a full-screen app covers its monitor, or shows it only then. It follows a change within a second. While the window is open, the overlay always shows, so every change can be seen.
- R69. Two hotkeys, off by default, are set in the window: one hides and shows the overlay, the other moves to the next saved style, in name order, starting from the first when the style is not a saved one, and doing nothing when there are none. A combination that another app holds is refused with that reason, also when it was written by hand in the file.
- R70. The hide hotkey overrides the visibility setting until the next time a full-screen app comes or goes. A hidden overlay also comes back by opening the window or starting thermi again, and every start begins without the override.

Language

- R71. Every text a user reads comes from the translations: the window, the overlay's menu and messages, the tray tooltips and the default labels. Sensor and device names, units and messages from Windows itself are shown as they come; the command line, the log and the service stay in English. English and Spanish ship.
- R72. thermi uses Windows' display language when it has a translation for it, English otherwise. The window can choose another, which applies to the window and the overlay at once, and is undone like any change.
- R73. Numbers follow Windows' regional format, whatever the language.
- R74. Every translation has every text the English one has, and no others.
- R75. The window holds texts 40 % longer than English without cutting or overlapping them.

The tray

- R76. thermi shows as the overlay (the default), as an icon in the notification area, or both. The icon's right-click opens the overlay's menu, a double-click opens the window, and its tooltip gives every item's label and value, one line per pill, cut at a whole line with "…" when it is too long.
- R77. Any item can also show as a tray icon of its own: its value without unit or separators, up to 4 digits (10,000 and over as "10k", decimals dropped, "–" when missing), colored by its meter's level like the overlay, or in the taskbar's text color without a meter. Its tooltip is the item's label, value and unit.
- R78. Tray icons follow the taskbar's mode and the monitor's scale, refresh once a second with the overlay, and come back on their own when Explorer restarts.
- R79. Each tray icon keeps its identity across restarts, so Windows remembers where the user pinned it. New icons start in the hidden area, as Windows decides, and the window says so and opens the taskbar settings where they are pinned.

## Design

**Two processes, one file** (`docs/adr/0005-settings-window-process.md`). `thermi settings` runs the window in its own process of the same `thermi.exe`, on an STA thread of its own (the overlay's entry point is async), and WPF loads only there. It reads the sensors from the pipe like the overlay does, and talks to the overlay only through the settings file.

**Writes.** Every writer (the window, and the overlay when dragged) holds a named mutex for the settings file from the moment it re-reads the file until its write is moved into place. The work under it is synchronous, never across an `await`, and the mutex is released by the thread that took it. It is polled every 50 ms on the injected clock, never waited on with the real one. If the re-read under it finds the file locked, empty, cut short or missing, the writer releases it, waits and tries again, as a read does; a file still missing after that is written fresh. If it finds the file broken, it writes nothing, as spec 001 (R7) requires. A move into place that fails counts like a timeout. Waiting for it gives up after 2 s, and the write is tried again through the throttle until it lands; the overlay writes off its UI thread, so the overlay never waits. A mutex left behind by a writer that died is acquired by the next one. Every writer, on taking the mutex, deletes temporary files left beside the settings file, since no live writer can have one then. The writer applies its own change to what it just read, and writes under a temporary name of its own. Readers open the file sharing read, write and delete, so a reader never blocks a move into place.

**Reads.** A read that finds the file locked, empty, cut short or missing is retried every 50 ms for up to 500 ms. Only then is it a problem, or defaults, or a missing file to recreate.

**Throttle.** Window writes and overlay reloads both go through one throttle in Core: at most one every 100 ms, and the last value always goes out. The overlay's reload moves from its 300 ms debounce to this throttle and the read retry, and its paints, its once-a-second refresh included, go through the throttle too. With `--trace`, both processes log on the same monotonic clock: the window logs each input, the overlay each paint. That is how R7 is measured, from input to paint.

**Item ids.** An item's id is kept in the file. Every id already in the file is reserved first, and going down the list the first item with an id keeps it. An item without one, or with one already taken, gets its metric or sensor id, or that id with the first free suffix (`cpu.temperature`, `cpu.temperature-2`). So every read of the same file gives the same unique ids, and the next write stores them. Default items, and so a reset, get ids the same way.

**Undo** keeps the window's changes as operations on item ids, each with what it found and what it left:

- **A field:** goes back only if it still holds what the window wrote.
- **An added item:** removed only if it is still as added.
- **A deleted item:** comes back at its old place, or at the end if that place is gone, unless an item with its id exists again.
- **A move:** goes back only if the item is still where it was moved.
- **A reset:** one step made of these operations, item by item and field by field.

Redo checks the same way. A step that cannot apply is skipped, and the window says so. The window remembers the content of its own last write, so the watcher event it causes is recognized and ignored. While a gesture is in progress, outside changes wait until it ends.

**History** lives in the window, from the moment it opens: one minute for sparklines and ten for the threshold histogram, with gaps kept as gaps. The service and the overlay keep none, so their budget does not move.

**The driver hint** in R16 and R23 is inferred from zeros, because a snapshot carries no driver state; adding it would change the pipe protocol and needs its own ADR.

**The fan finder** treats every fan sensor as a candidate, the GPU's included, and reads `cpu.load` during the run. Its tests replay a series recorded on a real PC. `thermi-sensors --dump <file> --seconds <n>` records one snapshot per second, one line each, in the pipe's line format. The series starts at least 5 s before a CPU load, holds it for 25 s, and runs 10 s after. `AGENTS.md` gives the load command. In a replay, the load starts at the first second whose CPU load passes 50 %.

**Settings file, version 2.** Fields are named, never inferred from a string's shape:

- `items`: each has an `id`, then either `metric` (picked per machine, as today) or `sensor` (a sensor id). An item made from a metric keeps it as `auto` when it names a sensor, so it can go back.
- `label` (optional): the label of a run of items. A sensor item gets one when it is added.
- `own` (optional): true when the item starts a pill of its own.
- `meter` (optional): `{ "metric": ... }` or `{ "sensor": ..., "auto": ... }`. `threshold` as today.
- `snap` (optional): `monitor` (left out for the primary monitor) and `spot`, one of `taskbar-left`, `taskbar-center`, `taskbar-right`, `top-left`, `top-right`, `bottom-left`, `bottom-right`. With both, `snap` wins. With neither, the overlay snaps to the right of the primary monitor's taskbar.
- `size` (optional): 0.75 to 2, 1 by default.
- `style` (optional), every field optional:
  - `name`: the saved style it came from, left out once anything else changes the look; the window shows "custom" without it, or when no saved style has that name;
  - `layout`: `pills` (the default), `each` or `one`;
  - `orientation`: `horizontal` or `vertical`;
  - `preset`: `compact`, `standard`, `large` or `custom`. Unless it is `custom`, it sets `label` and `value`, and those fields are ignored;
  - `label` and `value`: each a `size` and a `weight`;
  - `opacity`: `background` (0 to 1) and `overlay` (0.2 to 1);
  - `pills`: `dark`, `light` or `system`;
  - `colors`: `normal`, `warning` and `critical`, and `label` and `value` (left out for Auto), as `#RRGGBB`.
- `visibility` (optional): `always`, `hide-in-fullscreen` or `only-in-fullscreen`.
- `show` (optional): `overlay` (the default), `tray` or `both`.
- `tray` (optional, per item): true when the item has a tray icon of its own.
- `hotkeys` (optional): `toggle` and `nextStyle`, each like `Ctrl+Alt+T`.
- `language` (optional): a language code, or left out to follow Windows.

The presets, at 100 %: labels are semibold and values bold in all three; Compact is 11 and 12 px, Standard 13 and 13.5 px, as today, and Large 16 and 17 px.

The pills' colors, as outer, border, inner and unlit meter: dark is `#20201E`, `#31312F`, `#171715` and `#2E2E2C`, as today; light is `#F3F3F1`, `#DADAD8`, `#FFFFFF` and `#D5D5D3`. Auto text is `#A9A9A7` labels and `#EFEFEE` values on dark pills, as today, and `#5C5C5A` and `#1A1A1A` on light ones.

**A style file** is `{ "thermiStyle": 1, "style": { ... } }`, with the `style` object above and its `name`. A saved style is one such file in `%APPDATA%\thermi\styles`, named from its name with the characters Windows refuses replaced and a number added if the name is taken; the name inside is what counts. An export is the same file. Plain JSON, small enough to paste in a chat, readable by people and agents.

**The migration from version 1:**

- `sources` go to the items that show those metrics, and to the meters that name them, each keeping its metric as `auto`. A fan duty meter on a named fan becomes that fan's own duty; no meter is added where there was none.
- A source goes over as it is, even of the wrong type. An item whose sensor is not of its metric's type shows as missing, as in version 1, and the window marks it.
- A saved `position` stays a free position; without one, the overlay gets the default snap.

A v0.1 build reading a version 2 file reports it as broken or as written by a newer thermi, depending on its fields, and never overwrites it (spec 001, R7). Version 2 gets a recorded file of its own and a round-trip test, as ADR 0001 asks of every version.

**Files that change:**

- In `src/Thermi.Core`, new code for:
  - the throttle;
  - undo;
  - the edits;
  - the catalog;
  - the history;
  - the fan finder;
  - the snap math;
  - styles: presets, Auto colors and contrast, the style file;
  - the visibility rule;
  - the translations;
  - the tray texts: short values and tooltips.
- In `src/Thermi.Core`, existing files:
  - `OverlayConfig.cs`: version 2, ids, `own`, style, visibility, hotkeys, language and the migration;
  - `SensorMap.cs`: sensor items, the fan's own duty, and the zero rule limited to CPU sensors;
  - `OverlayLayout.cs`: labels, meters, and pills from the layout and `own`;
  - `Metrics.cs`: default labels come from the translations.
- In `src/Thermi`:
  - `Program.cs`: `settings` on its STA thread, `--trace` and `--pseudo`, all checked before the overlay's single-instance mutex; a second overlay asks the first to show and exits; `preview` reads version 2;
  - `Thermi.csproj`: WPF;
  - `OverlayWindow.cs`: the menu in the chosen language, double-click, snap and size, the drag distance, the throttled reload, the language and Windows' mode followed, visibility and hotkeys;
  - `Settings.cs` and `ConfigStore.cs`: the mutex, read retry and re-read before write;
  - `SensorClient.cs`: tells "service not running" from "data stale";
  - `Native.cs`: the drag distance, the notification area, monitor EDIDs, full-screen apps, hotkeys, tray icons and the window messages;
  - new `TrayIcons.cs`: the tray icons;
  - `Painter.cs`: where each item and value is drawn, so the stage can hit-test them; pills of one item, one bar, vertical stacking, sizes and weights, opacity with the text shadow, light pills, and text and meter colors.
- New: the window's own files.
- In `src/Thermi.SensorHost`, `Program.cs`: `--dump` records a series. `AGENTS.md` says how.
- `scripts/install.ps1` writes the start-up entry only on a first install, or when it was already there.
- Docs: `docs/QUICKSTART.md`, `docs/ARCHITECTURE.md` (the diagram, the names, the projects), the READMEs of `src/Thermi` and `src/Thermi.Core`, and `AGENTS.md`, whose rules table gains the translation keys rule.

**Pills and style** live in Core as data: the layout groups the items into pills from `own`, the labels and the style's layout, and the style resolves its preset into sizes and weights, and Auto or unreadable colors into readable ones. The painter only draws what it is given, so the stage and the overlay always agree. An imported style is applied like any change, so the overlay stays the preview and Undo is the way back.

**The window and the overlay** talk through the file for settings, and through two window messages for what is not a setting, posted to the overlay's hidden message window, found by its class name: "show" (the window opening, or a second thermi starting, which then exits) and "hotkey status", which returns which hotkeys the overlay could not register. Both are local to the user's session. ADR 0005 records it.

**Visibility** asks Windows whether a full-screen app is in front (`SHQueryUserNotificationState`) and whether the foreground window covers the overlay's monitor, once a second on the existing tick. A game in exclusive full screen draws over every window, so there the overlay cannot show at all; borderless and most modern games are not exclusive. The overlay shows when the window is open, or when the hide hotkey's override says so, or else when the visibility setting allows it; the override is cleared when a full-screen app comes or goes, and at every start. The hotkeys are registered by the overlay (`RegisterHotKey`), since it is the process that is always running, again whenever they change in the file; the next style is written through the settings file like any change.

**The tray** lives in the overlay's process, with `Shell_NotifyIcon`: one icon for thermi and one per item that asks for it, each with a GUID made from the item's id, so Windows keeps where it was pinned. Icons are drawn at the size Windows asks for, 16 px at 100 % and more at higher scales, in Segoe UI Variable condensed to fit 4 digits, and the old icon is destroyed after each refresh. `TaskbarCreated` brings them back after Explorer restarts. Which value and text each icon shows, and the tooltips, are decided in Core.

**Translations** are .NET resource files in `src/Thermi.Core`, one per language, with English as the fallback, so both processes share them. The window and the overlay read the language once at start and again when the setting changes. A repo rule checks that every translation has every key of the English one and no others. The long pseudo-language for R75 is not shipped: `thermi settings --pseudo` builds it from English when it starts, each text padded by 40 % with accented letters, so it has no file and no keys to check.

**The look** is Windows 11: Mica, Segoe UI Variable, rounded corners, the system accent, light and dark. One window of 1040 × 680 that shrinks to 760 × 560. The stage is on top, with the inspector for the selected item on the left and, on the right, three tabs: Placement (where and how thermi shows, the overlay or the tray), Style and General (language, visibility, hotkeys, start with Windows, the settings file, reset). An item's own pill and tray icon are switches in its inspector. The catalog opens as a panel from the right edge.

**Rejected:**

- Editing on the overlay itself, with handles on the taskbar. It is small, sits over the taskbar and must stay light; precise controls need room.
- WinUI 3. It needs the Windows App SDK runtime installed beside thermi, and costs more to start.
- Skins with images and fonts, as Rainmeter and RivaTuner allow. They need files beside the style and break when shared; thermi's styles are one small text file.
- Any font and spacing as free fields. Presets and a few bounded controls cover the looks people ask for; colors are free, but drawn only when readable.
- Profiles that switch items and style per game. They need game detection, which comes with FPS in 1.0; a hotkey through saved styles covers the need until then.
- Keeping history in the service, so the window opens with ten minutes already drawn. It costs memory all day for a window opened now and then, and changes the pipe protocol.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
