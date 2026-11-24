# 004-updates: Tasks

> Progress: 1 / 28
> Current block: B0

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decisions

- [x] **T1** Covers R1–R24: `docs/adr/0008-updates.md` rewritten for updates beside the running version, and this spec with it.
- [ ] **T2** The acceptance tests written, waiting with `Skip`.

## B1: Core, without Windows

- [ ] **T3** Covers R9: versions and their order, the envelope and the manifest, their caps and signature.
- [ ] **T4** Covers R11, R13: what the service takes from a manifest, rollout, withdrawal and failed versions.
- [ ] **T5** Covers R4, R5, R7, R8, R14, R16: when to check, which channel, metered, and the quiet moment.
- [ ] **T6** Covers R17: probation by polls, ticks and errors.
- [ ] **T7** Covers R21: the levers on a channel.
- [ ] **T8** Covers R23: the state file and About's line.

## B2: The layout

- [ ] **T9** Covers R15: the launchers, made by the SDK's `CreateAppHost` to start their version's folder; a self-contained publish per version.
- [ ] **T10** Covers R3, R4: `scripts/install.ps1` lays down the layout, with updates off or pointed at a test channel; "Start with Windows" and the tray icons' ids name the fixed path.
- [ ] **T11** Covers R10, R12: `src/Thermi.Trust`, the folder and Authenticode checks moved out of the driver's install.

## B3: The service's updater

- [ ] **T12** Covers R5, R6, R8: the check and the download.
- [ ] **T13** Covers R10, R12: verifying, unpacking and the folder checks.
- [ ] **T14** Covers R14, R15, R17, R18: the switch, the watchdog, the control codes, probation and pruning.
- [ ] **T15** Covers R20, R24: the event log entries, and the pipe's golden file.

## B4: The overlay and the window

- [ ] **T16** Covers R14, R16, R17: the quiet moment, code 128, the handover and code 129.
- [ ] **T17** Covers R19: the settings' way back.
- [ ] **T18** Covers R23: About's line, in every language.

## B5: The levers

- [ ] **T19** Covers R21, R22: `src/Thermi.Releases` and `scripts/updates.ps1`: keys, pack, publish, promote, rollback, disable, status, serve, over a folder or R2.
- [ ] **T20** Covers R21: CI publishes each merge to main to beta, once the repo holds the key.

## B6: Proof on this PC

- [ ] **T21** Covers R14–R17, R22, R24: the live run of A15 against a channel served from this PC.

## B7: Installer and release

- [ ] **T22** Covers R1, R3: the WiX MSI and its uninstall.
- [ ] **T23** Covers R2: the release workflow: approval, SignPath, the signed manifest, the MSIs, provenance.
- [ ] **T24** What the owner must create: the list, with each step.
- [ ] **T25** The docs: `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/QUICKSTART.md`, the READMEs and the privacy note.

## B8: Review and proof

- [ ] **T26** The reviewer agent round after round until it approves, and the comment-trimmer.
- [ ] **T27** A cold review by two agents, one on Fable and one on Opus, scored 1 to 10, until both pass.
- [ ] **T28** The `by hand` steps, and the spec moves to `done`.

## Decisions

- The owner approved the whole of this spec in advance on 2026-11-24 ("yo apruebo todo desde ahora"), since every spec is rewritten as a clean v1 before the first public release; each choice below is the recommended one.
- Side by side behind .NET's own app host rather than an MSI per update: measured on this PC, an app host written to start `v1\probe.dll` runs the runtime and every assembly from `v1\`, and after it is renamed aside and replaced by one for `v2\`, a process started before keeps loading from `v1\` while a new one runs `v2\`. A junction is not resolved by the host, so it was dropped.
- The tray icons keep their ids across versions because the executable's path never changes; Windows ties an icon's id to that path.
- The overlay judges its successor by its own ticks and the watchdog by its polls, never by the clock: Mi Basket's BUG-509 reverted good releases by judging a page on a clock that ran while it could not.
- One failure anywhere takes the whole PC back, so every session runs one version; a local user can use that to keep the PC on the previous version until the next release, and nothing more.
- Channel and on/off are policy values only, with no switch in the window: whoever changes them for a PC is its administrator.
- No release notes in the manifest and nothing that pops up: the window shows no text from the network, and an update is invisible by design.
- The levers are a .NET tool beside Core rather than PowerShell alone, so they and the service read and write the same manifest code.
