# 004-updates: Tasks

> Progress: 0 / 26
> Current block: none, waiting for approval

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decisions

- [ ] **T1** Covers R1, R2, R10: `docs/adr/0008-updates.md` accepted, with the signing provider chosen and its identity validated.
- [ ] **T2** Covers R10: the manifest's HSM key and its offline backup made; their public keys go into the service.
- [ ] **T3** Covers R20: a mockup of the General tab's Updates section, approved.
- [ ] **T4** The acceptance tests written, waiting with `Skip`.

## B1: Core, without Windows

- [ ] **T5** Covers R10, R11: the manifest, its signature check, size caps, and the version rules, with the MSI version mapping.
- [ ] **T6** Covers R5, R7, R8, R9, R21, R22: when to check, which release each channel takes, metered, offline and the policy.
- [ ] **T7** Covers R14, R17: when to apply, and the probation with its counts, retries and rollback.
- [ ] **T8** Covers R16: the pipe's line kinds, hello and update, read by old and new readers.
- [ ] **T9** Covers R18: the settings copy before a migration, and the rollback rule.

## B2: The installer

- [ ] **T10** Covers R1, R3: the WiX project, self-contained publish, one MSI per processor, major upgrade, the version mapping.
- [ ] **T11** Covers R12: `%ProgramData%\thermi` with its access, and the service's access for control codes.
- [ ] **T12** Covers R3, R4: `scripts/install.ps1` builds and installs the MSI, with updates compiled off; the first start in a profile turns on "Start with Windows".
- [ ] **T13** Covers R1: the CI builds both MSIs on every PR.

## B3: The service's updater

- [ ] **T14** Covers R5, R6, R8, R9: checks and downloads with BITS, the fixed address and its request.
- [ ] **T15** Covers R10, R11, R12: verification in order, the folder checks, the file held open until the install ends.
- [ ] **T16** Covers R14, R21, R22: the control codes, their rate, and the state file.
- [ ] **T17** Covers R14, R15, R17: applying, the overlay started again in each session, probation and rollback.
- [ ] **T18** Covers R16, R23: the hello and update lines on the pipe, and the event log entries; the service's memory measured.

## B4: The overlay and the window

- [ ] **T19** Covers R14, R15, R19: exiting for an update, the quiet moment, the menu items and the notice after.
- [ ] **T20** Covers R4, R9, R13, R20, R21, R22: the Updates section, its texts in every language, notes as plain text.
- [ ] **T21** Covers R18: the copy before migrating and the rollback rule in the settings store.

## B5: Releasing

- [ ] **T22** Covers R1, R2, R10: the release workflow: approval, signing through OIDC, the signed manifest, provenance, the GitHub release and the winget pull request.
- [ ] **T23** Covers R13: the rule that only the updater touches the network, in `RepoRulesTests` and `BannedSymbols.txt`, listed in `AGENTS.md`; the docs and the privacy note.

## B6: Review and proof

- [ ] **T24** The reviewer agent, round after round until it approves, a security review of the whole change, and the comment-trimmer.
- [ ] **T25** The `by hand` steps of `acceptance.json`, with a signed beta and a beta built to fail, on a test PC and on the author's.
- [ ] **T26** `acceptance.json` all `passes: true`, and the spec moves to `done`.
