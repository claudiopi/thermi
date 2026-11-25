# 005-fps: Tasks

> Progress: 0 / 14
> Current block: none, waiting for approval

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decision and data

- [ ] **T1** Covers R3, R10, R11: `docs/adr/0014-frames-from-etw.md` accepted, after a spike on the author's PC: a session of thermi's own sees a Direct3D 12 and a Vulkan game's presents, each FPS within 2 % of the game's own counter.
- [ ] **T2** Covers R12: `thermi-sensors --frames <file> --seconds <n>`, listed in `AGENTS.md`; the author records a Direct3D 12 game through a loading screen and a Vulkan game, trimmed into `tests/Thermi.Tests/Recordings`.
- [ ] **T3** The acceptance tests written, waiting with `Skip` or `test.fixme`.

## B1: Core, without Windows

- [ ] **T4** Covers R1, R5: `FrameTally`, a second's FPS and longest frame per process, and the 5 s at 0 FPS.
- [ ] **T5** Covers R2, R6, R11: the slots in a snapshot and `GameSensors.With`, by process or parent, absent or without values.
- [ ] **T6** Covers R1, R7: the kinds `FrameRate` and `FrameTime`, their format and room, `DeviceKind.Game`, the two metrics in `SensorMap` and the catalog, and their texts in every language.
- [ ] **T7** Covers R8, R9: lines that warn low, their defaults and order in `ConfigRules`, and FPS at the end of the default overlay.

## B2: The service

- [ ] **T8** Covers R3, R10, R11: the ETW session in a new folder, `Frames`, of `src/Thermi.SensorHost`, started with the first client and stopped with the last, a stale one stopped first, no free session logged and tried again a minute later; the slots in each read.

## B3: The overlay and the window

- [ ] **T9** Covers R2, R4: the process in front on the overlay's and the window's tick, past thermi's own windows.
- [ ] **T10** Covers R7, R8: the chart draws and drags a low kind's lines, the critical below.

## B4: Review and proof

- [ ] **T11** The comment-trimmer, `pwsh scripts/check.ps1` green, and the reviewer agent round after round until it approves.
- [ ] **T12** Covers R10: `scripts/measure.ps1` on the installed build with a game presenting at 144 FPS.
- [ ] **T13** The `by hand` steps of `acceptance.json` on the author's PC.
- [ ] **T14** `acceptance.json` all `passes: true`, the docs updated, and the spec moves to `done`.
