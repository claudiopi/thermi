# 003-agents: Tasks

> Progress: 0 / 14
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

## B4: Docs, review and proof

- [ ] **T11** Covers R16: the README's agent section, with the Claude Code and Claude Desktop setup; `AGENTS.md`, `docs/ARCHITECTURE.md` and `docs/QUICKSTART.md`.
- [ ] **T12** The comment-trimmer, `scripts/check.ps1`, the reviewer round after round until it approves.
- [ ] **T13** Covers R16: the `by hand` steps, with Claude Code and Claude Desktop on the author's PC.
- [ ] **T14** `acceptance.json` all `passes: true`, and the spec moves to `done`.
