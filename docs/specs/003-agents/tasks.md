# 003-agents: Tasks

> Progress: 0 / 19
> Current block: none, waiting for approval

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decisions

- [ ] **T1** Covers R1, R14: `docs/adr/0013-agents-cli-and-mcp.md` accepted, with `CreateAppHost` and the MCP SDK tried in a spike.
- [ ] **T2** The acceptance tests written, waiting with `Skip`.

## B1: Core

- [ ] **T3** Covers R2, R6, R7, R8, R9, R10: the answers' records and JSON, the reads, samples and levels, from recordings.
- [ ] **T4** Covers R11: `PageEdits` moved to Core, and an agent's edit parsed, the threshold edit included.

## B2: The door

- [ ] **T5** Covers R1, R2, R3: `thermi-cli.exe` written by the build and the publish, help with no command, stdout, stderr and exit codes.
- [ ] **T6** Covers R3, R4, R5, R6, R7, R9, R10: `status`, `read`, `sensors` and `config`, one short connection a read.
- [ ] **T7** Covers R11, R12, R13: `edit`, written under the file's mutex.
- [ ] **T8** Covers R1: `scripts/install.ps1` installs it and puts thermi on the machine's `PATH`, `-Uninstall` takes it off.

## B3: MCP

- [ ] **T9** Covers R14, R15, R17: the server on stdio, its tools, undo and redo of its own edits.
- [ ] **T10** Covers R18: `scripts/measure.ps1` on the installed build; the notices.

## B4: The contract and the SDK

- [ ] **T11** Covers R19: `watch`, and the session's share of the pipe at 8.
- [ ] **T12** Covers R20, R21: the contract's page and JSON Schemas, `--schema`, and the tests that hold them.
- [ ] **T13** Covers R22, R23: the .NET SDK, packed in the CI, with the error that says how to install thermi.

## B5: Sensors alone

- [ ] **T14** Covers R24, R25: `scripts/install.ps1 -SensorsOnly` with the driver step; the MSI's two features agreed with spec 004.
- [ ] **T15** Follow-ups noted: the Node and Python packages, each a spec.

## B6: Docs, review and proof

- [ ] **T16** Covers R16: the README's agent section, with the Claude Code and Claude Desktop setup; `AGENTS.md`, `docs/ARCHITECTURE.md` and `docs/QUICKSTART.md`.
- [ ] **T17** The comment-trimmer, `scripts/check.ps1`, the reviewer round after round until it approves.
- [ ] **T18** Covers R16: the `by hand` steps, with Claude Code and Claude Desktop on the author's PC.
- [ ] **T19** `acceptance.json` all `passes: true`, and the spec moves to `done`.
