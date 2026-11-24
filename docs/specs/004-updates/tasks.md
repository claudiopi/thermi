# 004-updates: Tasks

> Progress: 25 / 28
> Current block: B7

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
  - Once a version passed, nothing the watchdog then fails to write fails it: a launcher a program holds is put back at the next check, and a state write is tried five times, then finished at the next start from the switch it still names. The state, the marks and the install's undo record in `update\` are files no user may open, so none can hold one against a write; users read the newest of its copies, `status-*.json`, each a new file. An overlay hands over to the installed version's own program, not the launcher at the top, so a launcher left old never starts the old version again.
  - A release build whose signers are missing never updates rather than skip the catalog; only a channel on this PC goes without one. The build job fails when a public value is missing, and those values are repository variables, which a job with no approval reads.
  - The levers open each channel's history only when the release or the backup key signed it, so whoever can write the bucket cannot have one sign what they planted; sequences are checked arithmetic. A revocation stays in every manifest after it, signed only by the backup key, until `unrevoke`; docs/RELEASES.md has the recovery.
  - A switch's rollback is the switch it wrote down as one, not one to a lower version; a version ready below the one installed never switches.
  - A server failure stops the service with an error, which is a crash of a version on probation, not a stop asked for.
  - A download that moves nothing for a minute ends, on the injected clock; one that moves slowly is never cut. A check that cannot reach the channel still counts, so About tells a PC that never could.
  - The watchdog connects to the pipe within a second a poll, so a version that never serves goes back in about four minutes, and lets the pipe's server only identify it; an install waits fifteen for its lock.
  - Round 5 and the cold re-scores:
    - A start Windows timed out is no crash: the switch is dropped and tried again, and a switch the start asked for waits a minute, for a boot's disk and antivirus.
    - The MSI's commit runs `--forget`, which drops the undo record, so no later install's rollback puts back an older one; the record no longer carries a version.
    - A zip refused for what it holds is remembered by its SHA-256 in the state and never fetched again; a download that throws is tried at the next check.
    - Each lever's new manifests must open as a PC opens them, `ManifestEnvelope.Open`, the one rule for a revoked channel and any other invalid one. A history gone while its channel serves stops every lever, and a signed manifest is no history. An older signed history replayed only makes a lever sign a sequence PCs refuse, which withholds updates, as whoever holds the bucket can anyway.
    - `channel-sync.ps1` puts each history and manifest on the condition of the ETag it fetched, so of two runs at once the second fails whole.
    - `keys` never writes over a key, and About and the service share one rule for whether a build updates, `UpdatePolicy.Updates`.
    - A release run again after a refused upload puts its MSIs back on the GitHub release it made.
  - The unpacker counts what it writes, not what the zip declares, and flushes each file to the disk.
  - Left as they are, each written down in the ADR or docs/RELEASES.md: whether SignPath's certificate is thermi's own or shared, which the owner asks; a PC that skips the release listing a renewed certificate's thumbprint, which needs a later MSI; a user taking the pipe's name as a switch starts, which only puts it off; a withdrawal by a stolen manifest key, which sends PCs one version back. The watchdog's steps are still proven by the live run, A15, rather than a test: `ServiceConfig` drives the real service manager.
