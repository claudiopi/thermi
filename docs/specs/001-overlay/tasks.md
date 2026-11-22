# 001-overlay: Tasks

> Progress: 23 / 23
> Current block: none, done

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B1: Core, without Windows

- [x] **T1** Covers R2, R6: sensor snapshots, the stable metrics and the mapping from raw sensors, with zero meaning "not read" for CPU temperature and power.
- [x] **T2** Covers R3, R7: the versioned config, its defaults and a reader that never throws.
- [x] **T3** Covers R2, R3: the layout, label groups, meters and levels.
- [x] **T4** Covers R6, R8: the pipe's line format and the freshness rule (3 s).
- [x] **T5** Covers R6, R8: tests from recordings, including one made without admin rights on the author's PC.

## B2: The sensor service

- [x] **T6** Covers R1: `thermi-sensors` with LibreHardwareMonitor, one failing device never silencing the rest.
- [x] **T7** Covers R1, R8: the pipe server, its ACL, first-instance rule, client slots and slow-client drop.
- [x] **T8** Covers R1: a failure stops the service with an error code, so Windows restarts it.
- [x] **T9** Covers R1, R8: pipe and service tests over real pipes with a fake clock.

## B3: The overlay

- [x] **T10** Covers R2, R9: the layered window and the painter, one pill per group as the tools it replaces.
- [x] **T11** Covers R4: back above the taskbar whenever a taskbar rises over it.
- [x] **T12** Covers R5: dragging, the saved position, and back on screen when a monitor goes.
- [x] **T13** Covers R7: live reload, the problem shown in the overlay and its menu, and the file never overwritten while broken.
- [x] **T14** Covers R8: the client trusts only a pipe owned by the service, and no exception ends the overlay.

## B4: Install

- [x] **T15** Covers R1: `scripts/install.ps1` builds as the user and asks for admin rights once.

## B5: Review

- [x] **T16** The reviewer agent, round after round, until it approved, with every finding fixed or answered.
- [x] **T17** The comment-trimmer on the whole change.
- [x] **T18** Covers R2, R3: same look, units and thresholds as the tools it replaces, compared side by side on the author's taskbar.
- [x] **T19** Covers R6: a recording with admin rights from the author's PC replaces the hand-made one.

## B6: The author's daily use

- [x] **T20** The `by hand` steps of `acceptance.json` pass on the author's PC.
- [x] **T21** The old tools' scheduled tasks are turned off, once the author agrees.
- [x] **T22** A week of daily use without going back to the old tools.
- [x] **T23** `acceptance.json` all `passes: true`, and the spec moves to `done`.
