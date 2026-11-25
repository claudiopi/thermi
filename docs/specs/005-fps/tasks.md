# 005-fps: Tasks

> Progress: 10 / 17
> Current block: B0, decision and data, which waits for the author's spike and recording; B1 to B3 are done but for A1

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decision and data

- [ ] **T1** Covers R3, R10, R11: `docs/adr/0014-frames-from-etw.md` accepted, after a spike on the author's PC: a session of thermi's own sees Zwift's presents (OpenGL) and a Vulkan and a Direct3D demo's, each FPS within 2 % of PresentMon's beside it.
- [ ] **T2** Covers R12: `thermi-sensors --frames <file> --seconds <n>`, listed in `AGENTS.md`; the author records Zwift into a world through its loading screen, alt-tabbing to a browser and the desktop, trimmed into `tests/Thermi.Tests/Recordings`.
- [x] **T3** The acceptance tests written, waiting with `Skip` or `test.fixme`.

## B1: Core, without Windows

- [x] **T4** Covers R1, R5: `FrameTally`, a second's FPS and longest frame per process, and the 5 s at 0 FPS.
- [x] **T5** Covers R2, R6, R11: the slots in a snapshot and `GameSensors.With`, by process or parent, absent or without values.
- [x] **T6** Covers R1, R7: the kinds `FrameRate` and `FrameTime`, their format and room, `DeviceKind.Game`, the two metrics in `SensorMap` and the catalog, and their texts in every language.
- [x] **T7** Covers R8, R9: lines that warn low, their defaults and order in `ConfigRules`, and FPS at the end of the default overlay.

## B2: The service

- [x] **T8** Covers R3, R10, R11: the ETW session in a new folder, `Frames`, of `src/Thermi.SensorHost`, started with the first read and stopped 5 s after the last, a stale one stopped first, no free session logged and tried again a minute later; the slots in each read.

## B3: The overlay and the window

- [x] **T9** Covers R2, R4: the process in front on the overlay's and the window's tick, past thermi's own windows.
- [x] **T10** Covers R7, R8: the chart draws and drags a low kind's lines, the critical below.

## B3b: Voltages

- [ ] **T15** Covers R13: the kind `Voltage` in Core, its format, no offset and no lines, its texts in every language, its chip in the catalog, and A8 on the recorded desktop with the board's voltages; the author's next dumps, as administrator and without, replace those voltages with recorded ones.
- [x] **T16** Covers R2: FPS follows the game, not the focus: `FrontGame` takes a new game only after 3 s in front, filling its monitor, presenting at 30 or more and faster than the game while it presents (A1, A10).
- [x] **T17** Covers R3: a game's frames are counted while OBS captures it, from the kernel's `BlitRect`, counted apart from every other source, each second taking the one that told of most (A11, A12).

## B4: Review and proof

- [ ] **T11** The comment-trimmer, `pwsh scripts/check.ps1` green, and the reviewer agent round after round until it approves.
- [ ] **T12** Covers R10: `scripts/measure.ps1` on the installed build with a game presenting at 144 FPS.
- [ ] **T13** The `by hand` steps of `acceptance.json` on the author's PC.
- [ ] **T14** `acceptance.json` all `passes: true`, the docs updated, and the spec moves to `done`.
