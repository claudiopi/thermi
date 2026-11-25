# 003-agents: Tasks

> Progress: 15 / 20
> Current block: B6

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decisions

- [x] **T1** Covers R1, R14: `docs/adr/0013-agents-cli-and-mcp.md` accepted, with `CreateAppHost` and the MCP SDK tried in a spike.
- [x] **T2** The acceptance tests written, waiting with `Skip`.

## B1: Core

- [x] **T3** Covers R2, R6, R7, R8, R9, R10: the answers' records and JSON, the reads, samples and levels, from recordings.
- [x] **T4** Covers R11: `PageEdits` moved to Core, and an agent's edit parsed, the threshold edit included.

## B2: The door

- [x] **T5** Covers R1, R2, R3: `thermi-cli.exe` written by the build and the publish, help with no command, stdout, stderr and exit codes.
- [x] **T6** Covers R3, R4, R5, R6, R7, R9, R10: `status`, `read`, `sensors` and `config`, one short connection a read.
- [x] **T7** Covers R11, R12, R13: `edit`, written under the file's mutex.
- [x] **T8** Covers R26, R27, R28, R29: `looks` and `look`, `monitors` and `place`, `find-cpu-fan`, `start-with-windows`, sharing the window's code.
- [x] **T9** Covers R1: `scripts/install.ps1` installs it and puts thermi on the machine's `PATH`, `-Uninstall` takes it off.

## B3: MCP

- [x] **T10** Covers R14, R15, R17: the server on stdio, its tools, undo and redo of its own edits.
- [ ] **T11** Covers R18: `scripts/measure.ps1` on the installed build; the notices.

## B4: The contract and the SDK

- [x] **T12** Covers R19: `watch`, and the session's share of the pipe at 8.
- [x] **T13** Covers R20, R21: the contract's page and JSON Schemas, `--schema`, and the tests that hold them.
- [x] **T14** Covers R22, R23: the .NET SDK, packed in the CI, with the error that says how to install thermi.

## B5: Sensors alone

- [ ] **T15** Covers R24, R25: `scripts/install.ps1 -SensorsOnly` with the driver step; the MSI's two features agreed with spec 004.
- [x] **T16** Follow-ups noted: the Node and Python packages, each a spec.

## B6: Docs, review and proof

- [x] **T17** Covers R16: the README's agent section, with the Claude Code and Claude Desktop setup; `AGENTS.md`, `docs/ARCHITECTURE.md` and `docs/QUICKSTART.md`.
- [ ] **T18** The comment-trimmer, `scripts/check.ps1`, the reviewer round after round until it approves.
- [ ] **T19** Covers R16: the `by hand` steps, with Claude Code and Claude Desktop on the author's PC.
- [ ] **T20** `acceptance.json` all `passes: true`, and the spec moves to `done`.
