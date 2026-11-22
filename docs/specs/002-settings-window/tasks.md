# 002-settings-window: Tasks

> Progress: 0 / 40
> Current block: waiting for approval

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decision and data

- [ ] **T1** Covers R38, R51: `docs/adr/0005-settings-window-process.md` accepted.
- [ ] **T2** Covers R21: `thermi-sensors --dump <file> --seconds <n>` records one snapshot per second, with `AGENTS.md` giving it and the load command; a series is recorded on the author's PC around a CPU load, as the spec's design says.

## B1: Core, without Windows

- [ ] **T3** Covers all: the automated acceptance tests of `acceptance.json`, written first, waiting with `Skip`.
- [ ] **T4** Covers R43, R44: settings version 2 (item ids, metric or sensor, labels, `own`, meters, snap, size, style, visibility, hotkeys, language), ids that are the same on every read, the migration from version 1, and a recorded version 2 file.
- [ ] **T5** Covers R7: the throttle, on the injected clock.
- [ ] **T6** Covers R8, R9, R53: undo as operations by item id, skipped when the field no longer holds what was written.
- [ ] **T7** Covers R11, R17, R18, R26, R27, R28, R29, R36, R37: the edits, each returning valid settings or the reason it cannot.
- [ ] **T8** Covers R13, R14, R16: the catalog, its search and its "why" rules, with the zero rule limited to CPU sensors and shared with the sensor map.
- [ ] **T9** Covers R15, R25: the history, with gaps, on the injected clock.
- [ ] **T10** Covers R19, R21, R22, R23: the fan finder, replaying the recorded series and the no-admin recording.
- [ ] **T11** Covers R30, R31, R32, R34: the snap math, replacing today's default position.
- [ ] **T12** Covers R54, R55, R56, R57, R58, R59, R61, R62, R63: pills and style as data: `own`, the layout, orientation and its snap, presets, sizes and weights, opacity and its shadow, Auto and readable colors.
- [ ] **T13** Covers R71, R72, R73, R74: the translations in English and Spanish, the default labels from them, the language choice, numbers in the regional format, and the repo rule that compares the keys.
- [ ] **T14** Covers R68, R70: the visibility rule with the window and the hide hotkey's override, on the injected clock.
- [ ] **T15** Covers R76, R77, R79: the tray texts: short values, colors, tooltips and icon ids.
- [ ] **T16** Covers R63, R64, R65, R66, R67, R69: saved styles and their file names, the name a style remembers, the `.thermi-style` file, import with its refusals, and the next saved style.

## B2: The overlay

- [ ] **T17** Covers R17, R21, R28, R29, R32, R36: the overlay draws sensor items, a fan's own duty, labels, snaps and size from version 2 settings.
- [ ] **T18** Covers R7, R38, R39: the settings mutex (with a timeout, and taken over from a dead writer), read retry for locked, empty, cut short or missing files, and re-read before write; the reload through the throttle; `--trace` in both processes.
- [ ] **T19** Covers R33: a drag only past the system's drag distance; a click keeps the snap.
- [ ] **T20** Covers R56, R59, R60, R61, R62: the painter draws vertical overlays, opacity with the shadow, light pills, and text and meter colors.
- [ ] **T21** Covers R60, R68, R69, R70, R72: the overlay hides and shows by its visibility and its hotkey, registers the hotkeys and answers which it could not, answers "show", follows the taskbar's mode, and changes language without restarting.
- [ ] **T22** Covers R76, R77, R78, R79: the tray icons, drawn at Windows' size, restored when Explorer restarts.
- [ ] **T23** Covers R1, R2, R3, R4: "Settings…" replacing "Edit settings", double-click, `thermi settings`, one window at a time.

## B3: The window

- [ ] **T24** Covers R5, R6, R49, R50, R51: the WPF shell in its own process, with Mica, light and dark, accent, high contrast and reduced motion, its log line for the first frame, and the offer to start the overlay.
- [ ] **T25** Covers R10, R11, R12: the stage with the overlay's painter: select, drag, Alt+arrows, Delete, "+" and double-click.
- [ ] **T26** Covers R24, R25, R26, R27, R28, R29: the inspector: sensor, label, meter, and the threshold track with its histogram.
- [ ] **T27** Covers R13, R14, R15, R16, R17, R18: the catalog panel.
- [ ] **T28** Covers R19, R20, R21, R22, R23: the fan finder: the load, cancel, and the result as an undoable change.
- [ ] **T29** Covers R30, R31, R34, R35, R36, R52, R53: placement: the monitor map, size, "Start with Windows" (kept by `scripts/install.ps1`) and "Reset to defaults".
- [ ] **T30** Covers R8, R9, R42: undo and redo on Ctrl+Z, Ctrl+Y and the title bar, gestures as one step, and the window's own writes recognized.
- [ ] **T31** Covers R53, R54, R55, R57, R58, R59, R60, R61, R62, R68, R69, R72, R76, R77, R79: the Style and General tabs, where thermi shows and the tray hint, each item's tray switch, and dragging an item out of its pill on the stage.
- [ ] **T32** Covers R64, R65, R66: saving, renaming and deleting styles in the Style tab; export, drop and paste, with Keep and Undo.
- [ ] **T33** Covers R40, R41, R45, R46: a broken file, restoring, and the sensor service's state, told apart in `src/Thermi/SensorClient.cs`.

## B4: Finish

- [ ] **T34** Covers R47, R48: a pass with the keyboard only and one with Narrator.
- [ ] **T35** Covers R71, R75: a pass in Spanish and one with `--pseudo`.
- [ ] **T36** Covers R76, R78, R79: a pass with tray icons: pinning, scale, light taskbar and an Explorer restart.
- [ ] **T37** Covers R51: the overlay's CPU and memory measured while the window is used.
- [ ] **T38** The comment-trimmer, then the reviewer agent until it approves.
- [ ] **T39** The `by hand` steps pass on the author's PC.
- [ ] **T40** `acceptance.json` all `passes: true`, and the spec moves to `done`.
