# 004-updates: Tasks

> Progress: 26 / 35
> Current block: B10

Each `T` cites the requirements of `spec.md` it covers, and is ticked when it is closed.

## B0: Decisions

- [x] **T1** Covers R1–R24: `docs/adr/0008-updates.md` rewritten for updates beside the running version, and this spec with it.
- [x] **T2** The acceptance tests written, waiting with `Skip`.

## B1: Core, without Windows

- [x] **T3** Covers R9: versions and their order, the envelope and the manifest, their caps, expiry, signature and the backup key.
- [x] **T4** Covers R11, R13: what the service takes from a manifest, rollout, withdrawal and failed versions.
- [x] **T5** Covers R4, R5, R7, R8, R14, R16: when to check, which channel, metered, and the quiet moment.
- [x] **T6** Covers R17: probation by polls, ticks and errors.
- [x] **T7** Covers R21: the levers on a channel.
- [x] **T8** Covers R23: the state file and About's line.

## B2: The layout

- [x] **T9** Covers R15: the launchers, made by the SDK's `CreateAppHost` to start their version's folder; a self-contained publish per version.
- [x] **T10** Covers R3, R4: `scripts/install.ps1` lays down the layout, with updates off or pointed at a test channel; "Start with Windows" and the tray icons' ids name the fixed path.
- [x] **T11** Covers R10, R12: `src/Thermi.Trust`, the folder and Authenticode checks moved out of the driver's install.

## B3: The service's updater

- [x] **T12** Covers R5, R6, R8: the check and the download.
- [x] **T13** Covers R10, R12: verifying, unpacking and the folder checks.
- [x] **T14** Covers R14, R15, R17, R18: the switch through the services' image paths, the watchdog, code 128, probation, repair at start and pruning by renaming aside.
- [x] **T15** Covers R20, R24: the event log entries, and the pipe's golden file.

## B4: The overlay and the window

- [x] **T16** Covers R14, R16, R17: the quiet moment, code 128, and the handover, which gives up after 60 ticks.
- [x] **T17** Covers R19: the settings' way back.
- [x] **T18** Covers R23: About's line, in every language.

## B5: The levers

- [x] **T19** Covers R21, R22: `src/Thermi.Releases` and `scripts/updates.ps1`: keys, pack, publish, promote, rollback, disable, refresh, status, serve, over a folder or R2.
- [x] **T20** Covers R21: CI publishes each merge to main to beta and signs the channels again monthly, each behind the owner's approval.

## B6: Proof on this PC

- [x] **T21** Covers R14–R17, R22, R24: the live run of A15 against a channel served from this PC.

## B7: Installer and release

- [x] **T22** Covers R1, R3: the WiX MSI and its uninstall.
- [x] **T23** Covers R2: the release workflow: approval, SignPath, the signed manifest, the MSIs, provenance.
- [x] **T24** What the owner must create: the list, with each step.
- [x] **T25** The docs: `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/QUICKSTART.md`, the READMEs and the privacy note.

## B8: Review and proof

- [ ] **T26** The reviewer agent round after round until it approves, and the comment-trimmer.
- [ ] **T27** A cold review by two agents, one on Fable and one on Opus, scored 1 to 10, until both pass.
- [ ] **T28** The `by hand` steps, and the spec moves to `done`.

## B9: About's check

- [x] **T29** Covers R25: opening About asks the service for a check, its line says it checks, and Restart to update switches the version ready; A16 to A18 first, red, then A19 live.

## B10: Without administrator rights: the decisions

- [x] **T30** Covers R26–R33: `docs/adr/0016-admin-on-demand.md`, the requirements and their acceptance steps.

## B11: The user's host

- [ ] **T31** Covers R27, R28: Core's choice of pipe and the overlay's owner check, A20 red first; `thermi-sensors --user` on the user's pipe, ending with its last client, A21.
- [ ] **T32** Covers R32: the updater on the user's folder, its watchdog as the user, and the three named events, A23 and A24.

## B12: Install for everyone

- [ ] **T33** Covers R29, R33: the manifest's MSI entries, A25; the download, its checks and `msiexec` started unelevated, A22; the fix's offer in the overlay and the window.
- [ ] **T34** Covers R26, R30, R31: the per-user MSI, `ADDDRIVER`, the move to the machine install and the removal of the per-user product, A26 and A29.

## B13: Proof

- [ ] **T35** A27 and A28 by hand, the reviewer and the cold reviews again.

## Decisions
- The owner approved the whole of this spec in advance on 2026-11-24 ("yo apruebo todo desde ahora"), since every spec is rewritten as a clean v1 before the first public release; each choice below is the recommended one.
- Side by side behind .NET's own app host rather than an MSI per update: measured on this PC, an app host written to start `v1\probe.dll` runs the runtime and every assembly from `v1\`, and after it is renamed aside and replaced by one for `v2\`, a process started before keeps loading from `v1\` while a new one runs `v2\`. A junction is not resolved by the host, so it was dropped.
- The tray icons keep their ids across versions because the executable's path never changes; Windows ties an icon's id to that path. Measured: a running overlay whose executable is renamed aside still updates, removes and adds its icons.
- A cold review of the first design (Opus, 4/10) found that a power cut mid-switch or a dead watchdog left no service, that a user could fail any release for the PC, and that the manifest key alone could run code. So: the services' image paths name a version's folder and switch with one call of the service manager, pointed back at once until the new version serves; the switch is written down and finished or undone at the next start, which also repairs the launchers; an overlay that cannot switch only stays on its version in its session; the versions inside a zip must be the manifest's, and a release's signed catalog lists every file, so a manifest key alone neither runs code nor relabels an old release; manifests expire after 45 days, and a backup key held offline can revoke the release key.
- Pruning renames a version's folder aside first: measured, Windows refuses while any process runs from it, which is the test that no process uses it.
- The MSI owns a bootstrap and has its version adopt the layout, so a repair never fights the updater; Active Setup turns on the start at sign-in in each profile.
- The overlay judges its successor by its own ticks and the watchdog by its polls, never by the clock: Mi Basket's BUG-509 reverted good releases by judging a page on a clock that ran while it could not.
- Channel and on/off are policy values only, with no switch in the window: whoever changes them for a PC is its administrator.
- No release notes in the manifest and nothing that pops up: the window shows no text from the network, and an update is invisible by design.
- The levers are a .NET tool beside Core rather than PowerShell alone, so they and the service read and write the same manifest code.
- The live run on this PC (B6) found that a version whose service crashes as it starts made Windows refuse the start, which the watchdog took for a switch that never began: it dropped the switch, the old version tried again each minute, and each try counted as a crash of the old version, whose probation then sent the PC back a version. Once the watchdog points the service at the new version, that version ran, so a refused start now marks it failed, and the watchdog's own stop of the old version counts as a stop.
- About's line is worded in C#, in the window's language, and the page reads About again when the language changes.
- B6, live on this PC on 2026-11-24, against a channel served from it: 0.9.0 switched to 0.9.1, service and overlay, 5 s after the check, with no prompt; a 0.9.2 and a 0.9.4 whose service cannot start were failed and the PC stayed where it was (0.9.4, after the fix above, without counting a crash of the old version); a channel rolled back from 0.9.5 sent the PC back to 0.9.3 at its next check and failed 0.9.5; a turned-off channel left the PC on 0.9.3 with 0.9.6 offered; pruning removed every version neither kept nor running; the service held 65 MB private.
- The updater's acceptance tests run it whole against a channel served from the test's own loopback, on a fake clock that says when the updater waits, so a step never races a check. Only the folder's access and the watchdog's start are the test's own, through `FolderAccess` and `UpdateHost`: the tests run without admin rights, and an admin-only folder is one they could not write. The real access check still meets a folder users write to, and refuses it.
- A8 runs on the folder and the state file; which version Windows starts after a cut is the services' image path, which only an administrator changes, and the live run (A15) proved it on a real service.
- A service that cannot make its pipe because another program holds the name writes `update\pipe-held`, where no user writes, and the watchdog reads it before it fails a version: a user who squats the pipe, keeps it busy or denies SYSTEM it makes a switch be tried again later, never a version be failed. The lock the watchdog holds is made with access for SYSTEM and administrators alone, so no user can hold one a power cut left behind.
- A revocation sets the sequence back only when the backup key signed it, it names this build's release key, and that key is not revoked here yet; `revoke` takes the release key's public file and the backup's private one, and computes the hash PCs compare.
- A release's catalog is the one PowerShell's `New-FileCatalog` makes, which lists each file under its path with the hash Windows computes for its kind, and which the release signs with thermi's Authenticode key; the service reads it through Windows' own catalog calls, takes its SHA-256 members, and needs every file of the version listed as it is. A release build names the SHA-256 thumbprints of the certificates it accepts, `ThermiUpdatesSigners`, never a name: SignPath Foundation signs every open-source project it serves under its own one name, so a name would take any of their catalogs. A test build names none and needs no catalog. The catalog fails a version with a file it lists missing too, which Windows would look for elsewhere, and its signature is checked for revocation.
- The MSI is built with WiX 5, under the MS-RL with no fee: WiX 6 and later need their maintenance fee's EULA accepted, which is the owner's call. It is built outside `thermi.slnx`, from a version already published, by `scripts/updates.ps1 pack --msi`.
- The services are registered by the service's own code, `thermi-sensors --adopt`, through Windows' `sc.exe` by its full path, and `scripts/install.ps1` runs that too, so the MSI, the script and an update register them with the same code. The script's tests of its own registration moved to `InstallationTests`, on a faked service manager.
- An MSI installed, repaired or upgraded to while an update runs a later version keeps the later one, and Installed apps shows it: the MSI's own entry is hidden, and thermi's is written by `--adopt` and each finished switch.
- `--adopt` sets aside what a build from before the version folders left at the top, so `scripts/install.ps1` over one leaves only the layout.
- Windows Installer takes only a version of numbers, so a prerelease's MSI carries its numbers alone. Major upgrades take the same version too, so two builds of one version are never both installed; a silent install, as Intune's, starts no overlay, and one by hand starts it for the person who ran it.
- The release workflow cannot run before the owner's accounts exist, so it is checked as YAML and by the levers it calls, which run on this PC; its first run is the owner's. A beta per merge is `<version>-beta.<run>`, and only a stable release builds MSIs, since Windows Installer sees every beta of a version as that version. SignPath signs the binaries, then the catalog made over them, then the MSIs, each job behind an approval of the `release` environment. Merges share one concurrency group and each of the owner's runs has its own, so a merge never cancels a release or the monthly signing; two runs that write one channel are caught when the second uploads, since `scripts/channel-sync.ps1` refuses a manifest the bucket changed since its run fetched it.
- The third review found that an install could change the services under a switch, that the MSI's rollback left what `--adopt` did, and that `--remove` trusted wherever it ran from. So: `--adopt` and `--remove` hold the switch's lock, waiting up to three minutes for a watchdog; `--adopt` writes what it found to `update\adopt-undo.json` first, puts it back itself when a step fails, and the MSI's rollback runs `--undo` to put it back; both run only from a folder of thermi's (`setup\`, `install-*\` or a version's) whose parent only administrators change, and remove only thermi's own entries; the MSI's folders are private properties. An install over a build from before the version folders, which only developers' PCs have, cannot be rolled back to it.
- A failed probation goes back from the service's start itself, not at a later tick: a version that ends within seconds of each start would never reach one. A watchdog asked to stop a service still starting waits for it to run.
- The fourth review found that an install's undo record outlived it, so the MSI's rollback could put back what an install before recorded; that an install kept a version ready below itself, which the PC would then switch down to and fail; and that a revocation server out of reach would refuse every version. So: an install deletes the record before anything, writes its own, bound to its version, before any change, and `--undo` and `--remove` do nothing without the switch's lock; an install by hand drops a version ready that is not later, through the same Core rule as a service started as another version; and a revocation that cannot be checked passes, as browsers hold it, the pinned certificate being the check.
- The cold reviews (Opus 4/10, Fable 5/10) found more, and the fifth round answers them:
  - Once a version passed, nothing the watchdog then fails to write fails it: a launcher a program holds is put back at the next check, and a state write is tried five times, then finished at the next start from the switch it still names. The state, the marks and the install's undo record in `update\` are files no user may open, so none can hold one against a write; users read the highest numbered of its copies, `status-<n>.json`, each a new file. An overlay hands over through the launcher at the top, which the tray icons' ids are tied to, only once its bytes are the installed version's own `launchers\thermi.exe`, so a launcher left old never starts the old version again.
  - A release build whose signers are missing never updates rather than skip the catalog; only a channel on this PC goes without one. The build job fails when a public value is missing, and those values are repository variables, which a job with no approval reads.
  - The levers open each channel's history only when the release or the backup key signed it, so whoever can write the bucket cannot have one sign what they planted; sequences are checked arithmetic. A revocation stays in every manifest after it, signed only by the backup key, until `unrevoke`; docs/RELEASES.md has the recovery.
  - A switch's rollback is the switch it wrote down as one, not one to a lower version; a version ready below the one installed never switches.
  - A server failure stops the service with an error, which is a crash of a version on probation, not a stop asked for.
  - A download that moves nothing for a minute ends, on the injected clock; one that moves slowly is never cut. A check that cannot reach the channel still counts, so About tells a PC that never could.
  - The watchdog connects to the pipe within a second a poll, so a version that never serves goes back in about four minutes, and lets the pipe's server only identify it; an install waits fifteen for its lock.
  - Round 5 and the cold re-scores:
    - A switch the start asked for waits a minute, for a boot's disk and antivirus; a start Windows gave up on is taken up in round 6.
    - The MSI's commit runs `--forget`, which drops the undo record, so no later install's rollback puts back an older one; the record no longer carries a version.
    - A zip refused for what it holds is remembered by its SHA-256 in the state and never fetched again; a download that throws, or a version folder in use, is tried at the next check.
    - Each lever's new manifests must open as a PC opens them, `ManifestEnvelope.Open`, the one rule for a revoked channel and any other invalid one. A history gone while its channel serves stops every lever, and a signed manifest is no history. An older signed history replayed only makes a lever sign a sequence PCs refuse, which withholds updates, as whoever holds the bucket can anyway.
    - `channel-sync.ps1` puts each history and manifest on the condition of the ETag it fetched, so of two runs at once the second fails whole.
    - `keys` never writes over a key, and About and the service share one rule for whether a build updates, `UpdatePolicy.Updates`.
    - A release run again after a refused upload puts its MSIs back on the GitHub release it made.
  - Round 6 and its re-scores:
    - A start Windows gives up on, error 1053, cuts the switch as a power cut does: tried once more, then the version fails, so one that never starts in time does not stop the sensors each quiet minute.
    - Only a refusal for what the zip holds is remembered, and another version installed, by a switch or by hand, forgets it, since that build may trust what this one could not. An unreachable revocation server, as an unknown one, does not refuse a signed catalog.
    - The users' copies of the state are numbered, so the newest is the highest whatever the clock does, and a copy cut short goes with the others.
    - `keys` writes all four files or none; `channel-sync.ps1` fetches the signed files whole and needs AWS CLI 2.22, for its conditional puts; `install.ps1` runs `--forget` after its adopt, as the MSI's commit does.
    - A switch that went back without failing, cut by a 1053, refused at shutdown (1115) or met by a held pipe, keeps its version ready but begins no switch forward for a check's interval, `UpdateState.SwitchAfter`: not at the old service's start the watchdog made, nor at a quiet code a user sends, nor at an urgent check, so a program holding the pipe gets one try every four hours and no zip is fetched again. A rollback is never held. A check begins its switch before it prunes, and a top launcher it cannot read prunes nothing. Prune keeps the version the top launcher still starts; the overlay compares the launchers only when either changed, opened for delete too; it reads a copy again when its write changed. The stop's mark is written first, inside Windows' seconds at shutdown. `keys` makes each file new and deletes what it wrote when one fails. CRYPT_E_NO_REVOCATION_CHECK soft-fails as the other two revocation codes do.
    - Round 9: a hold further ahead than one interval, set by a clock that ran ahead, holds nothing, and an install by hand clears it; a start refused at shutdown, 1115, is held without counting as a cut; a top launcher that cannot be read is logged, and prunes nothing.
    - Live on the author's PC, from the rebased branch with a test channel served from it: the 0.9.7 MSI installed silently over a script install of spec 002, the settings and the start at sign-in untouched; 0.9.8 urgent switched in 4 s and the overlay handed over in 2 s; the channel rolled back took the PC back to 0.9.7 within the check and the overlay followed at its quiet moment; a 0.9.9 missing a library failed its start with 1053, as a .NET service that dies before it reaches Windows does, so it was held, the PC staying on 0.9.7. The service held 62 MB. Three fixes came of it: the service no longer logs each quiet code, an overlay sends none while the switch is held, and a failed start logs the reason Windows gave. An MSI over a thermi from before spec 004 leaves its running overlay as it was, which only a new sign-in or a start of thermi replaces.
    - Accepted: About says nothing of a refused zip, which the event log tells and only a later MSI mends; the watchdog and the overlay's handover are proven by A15 run live on the build, not by a fake of the service controller; a user who floods the sensors' pipe through a probation can withhold one version from one PC (ADR 0008); a start that fails for the PC's reasons other than 1053, as an antivirus blocking the program, fails that version there, which the next release mends; a catalog refused for a chain Windows could not build, as behind a proxy, is remembered until another version is installed.
  - The unpacker counts what it writes, not what the zip declares, and flushes each file to the disk.
  - Left as they are, each written down in the ADR or docs/RELEASES.md: whether SignPath's certificate is thermi's own or shared, which the owner asks; a PC that skips the release listing a renewed certificate's thumbprint, which needs a later MSI; a user taking the pipe's name as a switch starts, which only puts it off; a withdrawal by a stolen manifest key, which sends PCs one version back. The watchdog's steps are still proven by the live run, A15, rather than a test: `ServiceConfig` drives the real service manager.
- About checks as it opens, with no button, as Chrome's About page does, which the session of spec 002 decided for the owner on 2026-11-24. The ask is code 129, heeded at most once a minute by About and again by the service, so reopening it costs the channel one GET a minute at most. Its line says it checks until the service has checked since a minute before the ask, which the service would not have done again, and gives up after half a minute, as a check that hangs would leave it. A version ready offers Restart to update when its switch is not held: code 130 switches it as a quiet session's 128 does, without that code's minute, and the window closes, as a restart closes Chrome.
- Review round 11: About's restart waits out a check under way rather than drop the code, one restart waiting at a time; the users' copy of the state is looked for again when a newer one replaced it mid-read, as About now reads it each second; a start that throws is logged by the service, since with AutoLog off Windows logs no reason; a clock set back neither says it checks nor stops About from asking; the prune moved to InstallFolder; whether an overlay says its session is quiet is Core's UpdateSchedule.SaysQuiet. Accepted: no page test renders Restart to update, which only a real ready version shows; it is checked live in A19 and is the kit's accent button that axe passes elsewhere.
- Review round 12, accepted: the state copy's retry and the start's report have no test, since one needs a copy deleted between a listing and its read and the other a service's protected start; both are one catch each, read in review. One restart waiting at a time is read in review too: a test would need a check held open mid-way.
- A19 live on the author's PC, rebased onto spec 002's 0831f6d, its window driven over WebView2's debugging port with the owner's session disconnected: About said it checks, then 0.9.8 ready with Restart to update within 2 s. Closing the window let the disconnected session, which counts as locked, switch to 0.9.8. With 0.9.9 published, Restart to update began the switch within a second and closed the window, and the overlay handed over. Opened twice within a minute, About checked once. A rollback took the PC back to 0.9.8 at the new service's first check. Learned: an ask is heeded once a minute per service process, so a switch's restart lets the next ask through; and an install by hand under a later version kept the later one, as AdoptAsync says.
- Admin on demand, asked by the owner on 2026-11-25 through the session of spec 002 ("Sumalo y decile que lo haga sobre lo último"), since competitors sell "no admin needed": thermi installs per user without a prompt, and one prompt adds the machine install, the service and the driver. The user's host takes a pipe named for the user's SID rather than the service's name, which was the first ask: one user's host would hold the name against the service and every other session. The prompt runs the machine MSI through `msiexec`, not `sc.exe`, since a per-user install has no service to start yet; it names thermi's signed publisher. `docs/adr/0016-admin-on-demand.md` holds the rest.
- The design review of ADR 0016 changed it before any code. `msiexec` runs without `runas` and `/passive`, so its prompt names thermi's signer and the MSI's `StartOverlay` never runs elevated; the file is held open until it exits. The fix installs the version the channel offers when it is the running one or later, and opens the download page otherwise. The per-user switch places its launchers only once the new host serves. The per-user product has its own UpgradeCode and is removed by the machine's overlay after the handover. `ADDDRIVER` acts only on a first install, through the `thermi-elevated` service; the second review made it a secure property, since `msiexec` started unelevated by a standard user would otherwise drop it, and `NOT Installed` keeps a repair from adding the driver. The third review approved the design. The user's pipe has its owner set, and the overlay also checks the server's folder and integrity. The host's restarts back off. About's codes reach the user's host as named events rather than a pipe, and the user's host logs to a file.
