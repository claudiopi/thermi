# 004. thermi keeps itself up to date, safely

**Status**: draft

## Why

Today thermi is built and installed from the repo by `scripts/install.ps1`. A public release needs a signed installer people trust, and a way to get fixes to them without a UAC prompt or a visit to GitHub. The service that would install them runs as LocalSystem, so this is the riskiest change thermi makes: it must be impossible to make it install anything but a newer thermi, signed by its author.

The decisions, the threat model and the alternatives are in `docs/adr/0008-updates.md`.

## Requirements

Getting thermi

- R1. Each release is one signed MSI per processor, x64 and Arm64, on GitHub Releases and on winget. It installs thermi, its service and the start at sign-in, with no other download.
- R2. Every file the installer puts on disk has a valid Authenticode signature: thermi's, or its maker's for the .NET runtime.
- R3. Installing over an earlier version, by hand, through winget or by an update, keeps each user's settings, saved styles, log and "Start with Windows" choice.
- R4. A build made by `scripts/install.ps1` never checks for updates, and says so in the General tab.

Checking

- R5. The service checks for a new version 5 minutes after it starts, then once a day at a random time.
- R6. A check is one HTTPS GET of a fixed address, with no query, no cookie, no identifier and `thermi` as the whole User-Agent.
- R7. The stable channel offers stable releases; the beta channel offers the newer of the latest beta and the latest stable. Going back to stable never installs an older version: thermi waits for a stable that is newer.
- R8. On a metered connection nothing downloads in the background; "Update now" still downloads.
- R9. Offline, or behind a proxy the service cannot pass, a check fails quietly and the next one tries again. After 14 days without a successful check, the General tab says since when, with a link to the releases page.

Trust

- R10. The service installs a file only when, checked on the downloaded file before anything installed changes: a manifest lists it, signed by a key built into thermi; it has the size and SHA-256 the manifest gives; and it is signed by thermi's pinned Authenticode identity.
- R11. The service never installs a version lower than or equal to the installed one or the highest it ever installed, and never accepts a manifest published before the last one it accepted. Its own rollback (R17) is the one exception.
- R12. Downloads and the update's state live in a folder only SYSTEM and administrators can write. Its owner, its access and the absence of links are checked before each use; a folder that fails is not used, nothing is installed, and the event log says why.
- R13. Nothing in thermi loads code, UI or markup from the network: the update is the only download. Release notes show as plain text.

Applying

- R14. A downloaded update applies, with no UAC prompt: when the service starts and no overlay runs, when a user picks "Update now", or when the only session with an overlay has had no input for 15 minutes and no full-screen app in front.
- R15. Applying closes thermi's overlays and window, installs, and starts the overlay again in each session that had one, as that user and unelevated. After "Update now", the overlay is back within 60 s.
- R16. Readings never stop because the overlay or the window and the service are of different versions: a service also serves the pipe protocol of the release before it, and an overlay reads both.
- R17. Within 10 minutes of an update, if the new service does not start, stops with an error twice, or no overlay of the new version connects within 2 minutes in a session that had one, the service reinstalls the previous version by itself, and the overlay says why. A version rolled back is tried again after 24 hours, at most 3 times.
- R18. A new version migrates each user's settings once, at its first start, keeping the earlier file as a copy. After a rollback, the earlier version starts from that copy, and keeps the newer file beside it.

The user

- R19. While an update is ready, the overlay's menu and thermi's tray menu show "Update to <version>". Nothing pops up before an update. After one, a single Windows notice says thermi is now <version>; clicking it opens what's new.
- R20. The General tab shows the version, the channel, whether updates are automatic, the last check, "Check now" or "Update now", and what's new in the ready or the last update.
- R21. With automatic updates off, nothing is checked or downloaded until "Check now". The channel and the switch are for the whole PC, the tab says so, and changing either asks for administrator rights once.
- R22. A policy value, `Updates` = 0 under `HKLM\SOFTWARE\Policies\thermi`, turns updates off and hides their switches and menu items. Installing by hand or through winget still works.
- R23. Each check, download, update, rollback and refusal goes to the event log under `thermi-sensors`. At rest, the service stays under the 80 MB of ADR 0011.

## Design

**The installer** is a WiX MSI, built in a new installer project in `src`, from the same Release publish as today but self-contained, so it needs no runtime installed and ships the one it was tested with. One MSI per processor, per-machine, into `C:\Program Files\thermi` as today, and a major upgrade on every release with one upgrade code, so winget, an update and a double-click are the same install. MSI compares three fields, so a SemVer version maps to `major.minor.(patch × 1000 + n)`, n being the beta number or 999 for a stable: 1.3.0-beta.2 is 1.3.2, 1.3.0 is 1.3.999. The MSI registers the service with today's recovery and event log source, creates `%ProgramData%\thermi` with SYSTEM and administrators as its only writers, and grants interactive users only query and user-defined control on the service. It writes nothing in a user's profile: the overlay's first start in a profile turns on "Start with Windows" unless that profile turned it off before (`src/Thermi/Ui/StartWithWindows.cs`). `scripts/install.ps1` builds the MSI unsigned, with updates compiled off, and installs it, so every developer install exercises the real installer.

**The release** is a GitHub Actions workflow on a version tag, in an environment that waits for the owner's approval. It runs `scripts/check.ps1`, signs thermi's files and the MSIs through OIDC with no secret stored anywhere, signs the manifest with a non-exportable HSM key, attaches build provenance, publishes the release and the manifest, and opens the winget pull request.

**The manifest**, `releases.json` at a fixed address on the project's GitHub Pages, lists the last 20 releases, each with its version, channel, notes in English and Spanish, and per processor its URL on GitHub Releases, size and SHA-256, plus when it was published. Beside it, `releases.json.sig` is an ECDSA P-256 signature of its exact bytes. The service holds two public keys: the HSM's, and an offline backup that can replace it. Both files are capped at 64 KB, the MSI at the size listed.

**The service's updater** lives in `src/Thermi.SensorHost`, apart from the sensors' code. It downloads with BITS as SYSTEM, which uses the machine's WinHTTP proxy and auto-detection, resumes across reboots and knows a metered network. It verifies in order: the manifest's signature and date, the version rules, the size and hash, then the Authenticode signature with `WinVerifyTrust`, pinned to the signer's validated identity rather than a thumbprint, since the signing service's certificates last days. It holds the verified file open, shared for reading only, until the install ends, so nothing can swap it between the check and `msiexec`. It never uses `%TEMP%`, which users can write, and runs `%SystemRoot%\System32\msiexec.exe` by full path. Decisions (channels, versions, retries, probation) are in Core, tested on a fake clock; BITS, `WinVerifyTrust` and `msiexec` are behind interfaces, so tests fake them.

**Commands** reach the service as user-defined service control codes, integers carried by Windows: 128 check now, 129 update now, 134 "this session is quiet", which the overlay sends for R14. A code carries no sender, so the service acts on 134 only while one session runs an overlay. Each code is acted on at most once a minute. The channel and the automatic switch are not codes: they live in `%ProgramData%\thermi\update\settings.json`, which only administrators can write, and the window changes them by starting `thermi updates` elevated, one UAC prompt. The service still never reads its pipe, and never reads a file a user can write: its state is `%ProgramData%\thermi\update\state.json`, readable by users.

**The pipe, version 2.** Its first line is `{"kind":"hello","protocol":2,"version":"…"}`, and `{"kind":"update",…}` lines carry the update's state, version and notes. A line without `kind` is a snapshot, as today. Readers ignore kinds and fields they do not know, and today's reader already drops lines without `sensors`, so v0.x overlays keep working (`src/Thermi.Core/Sensors/Snapshot.cs`). A change that breaks a reader takes a new pipe name, and the service keeps serving the old one for one more release.

**Applying.** The service sends an `update` line saying it is closing thermi; overlays and the window exit within 5 s, and Restart Manager closes any older one that does not understand. It records which sessions had an overlay, then starts `msiexec`, which outlives the service it stops. Windows Installer rolls back the whole install if the new service does not reach running, and the service reports running only once its sensors and pipe are up. The new service starts the overlay in each recorded session with `WTSQueryUserToken`, the session user's unelevated token, and the fixed path under Program Files.

**Probation and rollback.** Before applying, the service keeps the MSI of the installed version in its folder, downloaded and verified from the manifest if it does not have it; no update applies without it. For 10 minutes after an update, each start of the service counts itself before loading the sensors; a third start, or the overlay missing as R17 says, reinstalls the kept MSI. A rollback can only reach the exact version that was installed before.

**Settings.** `src/Thermi.Core/Settings/ConfigMigration.cs` already migrates on read. Before its first write of a new format, the overlay copies the file to `config.v<N>.json`, N being the old version, once. A version that finds the file newer than it understands and a `config.v<N>.json` of its own beside it renames the newer file to `config.newer.json`, uses its own copy, and says so. That rule ships in the first updatable release, so every later rollback has it.

**The window on WebView2.** If the window moves to React on WebView2 (ADR 0007, to be written), its built files are embedded in `thermi.exe` and served from memory to a host name of its own, with every other navigation refused. They are signed and updated with it, and nothing is fetched at run time. The WebView2 runtime is Windows' own, kept current by Windows.

**Files that change**, besides the new installer project, the release workflow and the updater's folder:

- `src/Thermi.SensorHost/Program.cs` and `src/Thermi.SensorHost/SensorPipeServer.cs`: the updater, control codes, the hello and update lines, the second pipe name when one exists;
- `src/Thermi.Core/Sensors/SnapshotLines.cs`: line kinds; Core gains the manifest, its signature, the version and channel rules, and the probation;
- `src/Thermi/Overlay/OverlayHost.cs` and `src/Thermi/Tray/TrayIcons.cs`: the menu item, the notice, the quiet moment, exiting for an update;
- `src/Thermi/Overlay/ConfigStore.cs`: the copy before migrating and the rollback rule;
- `src/Thermi.Settings.Web/src/settings/SettingsPage.tsx`: the Updates section; the translations gain its texts;
- `scripts/install.ps1`, `.github/workflows/ci.yml` (builds the MSI on every PR), `BannedSymbols.txt` and `RepoRulesTests`: no network API outside the updater, a rule `AGENTS.md` lists;
- the docs: `docs/ARCHITECTURE.md`, `docs/QUICKSTART.md`, the READMEs, and a privacy note saying what a check sends.

**Rejected:** Velopack. It updates well and quickly, but installs per user into `%LOCALAPPDATA%`, a folder the user can write; a LocalSystem service there could be replaced by any program the user runs (`docs/adr/0003-sensor-service.md`). The others are in the ADR.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
