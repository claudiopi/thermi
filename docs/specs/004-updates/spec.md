# 004. thermi keeps itself up to date, silently and safely

**Status**: draft

## Why

Today thermi is built and installed from the repo by `scripts/install.ps1`. A public release needs an installer people trust, and fixes that reach every PC without a UAC prompt, a visit to a website or a gap in the overlay, as Chrome updates itself; and its owner needs Mi Basket's levers to stage a release, stop it and take it back within a minute. The service that installs runs as LocalSystem, so it must be impossible to make it run anything but a release signed by thermi's author.

The decisions, the threat model and the alternatives are in `docs/adr/0008-updates.md`.

## Requirements

Getting thermi

- R1. Each release is one MSI per processor, x64 and Arm64, that installs thermi for the whole PC: the launchers, one version's folder, both services and the Start menu entry, with no other download, and turns on the start at sign-in once in each profile. Uninstalling removes every version and leaves each user's settings.
- R2. In a release, every file thermi installs or unpacks is listed in a Windows catalog signed by thermi's Authenticode identity, and every executable thermi builds carries that signature too.
- R3. Installing over a version, an update and a rollback each keep every user's settings, saved styles, log, "Start with Windows" and the tray icons they chose to show; only a rollback across a change of the settings' format returns to the settings as they were before it (R19).
- R4. `scripts/install.ps1` installs the same layout as the MSI. Its builds never check for updates unless it is given a manifest's address and public key, and the window says that this build does not update itself.

Checking

- R5. The service checks a minute after it starts, every 4 hours, and after Windows resumes when its last check is 4 hours old or more.
- R6. A check is one HTTPS GET of the channel's fixed address, with no query, no cookie, no identifier and `thermi` as the whole User-Agent.
- R7. Under `HKLM\SOFTWARE\Policies\thermi`, `UpdateChannel` = `beta` follows the beta channel, anything else the stable one, and `Updates` = 0 turns checking off.
- R8. On a metered network the service checks, but downloads only a version marked urgent; it downloads nothing without three times the zip's size free.

Trust

- R9. The service takes a manifest only when its envelope is at most 64 KB and signed by a key built into thermi, it names the PC's channel, its sequence is not below the last one taken, and it has not expired; a manifest expires at most 45 days after it was signed. One signed by the backup key may revoke the release key it names by SHA-256 and set the sequence back; one that expires further ahead than 45 days and a day is refused.
- R10. A zip is unpacked only when the file the service holds open has the size and SHA-256 the manifest gives, and a version runs only when the versions inside it are the manifest's and, in a release build, the signed catalog lists every file it holds. Any refusal leaves everything installed as it was, and the event log says which check failed.
- R11. The service never takes a version at or below the installed one, nor one that failed on this PC. When the manifest withdraws the installed version, the PC goes back to the previous version it kept, unless that one failed too, and stays where it is without one.
- R12. Every entry of a zip lands inside its version's new folder. The update reads and writes only under `C:\Program Files\thermi`, which it checks before each use to be a real folder that only administrators, SYSTEM and Windows' installer may change; a check that fails changes nothing and is logged.
- R13. The PC draws a number from 0 to 99 once and keeps it, and takes a version only when that number is below the manifest's rollout percentage.

Switching

- R14. A ready version switches when the service starts, when an overlay says its session is quiet (no settings window and no drag, and either three minutes without input, the session locked or every overlay hidden), or at once when the manifest marks it urgent. Nothing asks for administrator rights, and users can send the service no other command.
- R15. A switch changes only the services' image paths and the two launchers in `C:\Program Files\thermi`; a process that runs keeps its version until it switches. A power cut or a crash at any point of a switch leaves Windows starting the old version or the new one whole, and the service finishes or undoes the switch at its next start.
- R16. Once the service switched, each overlay switches at its own quiet moment, or for an urgent version as soon as its window is closed and no drag is under way, by starting the fixed launcher; when the new overlay does not come up within 60 of the old one's ticks, the old one comes back and leaves that version alone in its session.
- R17. The new service passes when it serves a snapshot within 120 polls of its pipe, one a second, and fails when it does not, or when a second run of it ends without stopping within 10 minutes of its switch. A failure takes the PC back to the previous version and marks the failed one, never to be taken again. Sleep never fails a version, and no user can.
- R18. The service keeps the installed version and the previous one, and deletes every other version's folder once Windows lets it rename that folder aside. It writes its state whole or not at all.
- R19. A version that finds the settings in a newer format than it reads, with the copy it kept of them in its own format beside them, reads that copy and keeps the newer file beside it.
- R20. A service serves the pipe of the release before it: the lines it writes for a recording are held to a golden file, which a change of format must replace on purpose.

The owner

- R21. `scripts/updates.ps1` publishes a build to a channel with a rollout and an urgent flag, promotes beta's version to stable, rolls a channel back, turns it off, signs it again before it expires and shows every channel; each is a new manifest signed with the next sequence. `publish` and `promote` refuse a version below the channel's, and a version reaching stable reaches beta too when beta is behind. A merge to main publishes to beta, and each publish waits for the owner's approval.
- R22. The manifests are served with a 60-second cache and the zips as immutable, so a lever reaches a PC at its next check.

The user

- R23. About shows the version, and one line on updates: up to date and when it last checked, a version ready and that it switches when the PC is quiet, updates turned off by the PC's policy, the beta channel, no successful check for 14 days, or a build that does not update itself. Nothing else ever shows an update.
- R24. Each check that finds a version, each download, switch, rollback and refusal goes to the event log under `thermi-sensors`, and the service stays under the 80 MB of `docs/adr/0011-sensor-service-memory-budget.md`.

## Design

**Layout.** `C:\Program Files\thermi` holds `thermi.exe` and `thermi-sensors.exe`, .NET app hosts that the build writes to start `<version>\thermi.dll` and `<version>\thermi-sensors.dll`; a folder per kept version, each a self-contained publish of both programs with its own launchers in `<version>\launchers\`; and `update\`, with the service's `state.json`, its downloads and the files it renamed aside. The services' image paths name `<version>\thermi-sensors.exe`. The build makes the launchers with the SDK's own `CreateAppHost` task, from the app host and resources it gives the program, in `src/Launchers.targets`, which both programs import. `src/Thermi/Ui/StartWithWindows.cs` writes the launcher's path rather than its own process's, and the tray icons' ids come from it.

**Core** gains `src/Thermi.Core/Updates`: versions and their order (`ReleaseVersion.cs`), the envelope and the manifest and their checks (`Manifest.cs`), what the service takes from a manifest given its state (`UpdatePlan.cs`), the policy, when to check and the quiet moment (`UpdateSchedule.cs`), the health of a switch by counted polls, ticks and starts (`SwitchHealth.cs`), the state file with its switch written down (`UpdateState.cs`), the levers on a channel (`Levers.cs`), and the line About shows (`UpdateStatus.cs`). Each is pure and table-tested.

**Trust on Windows** moves to a library of its own, `src/Thermi.Trust`, since the driver's install and the updater now both need it: whether a folder is one only administrators change (today in `src/Thermi/Commands/DriverFolder.cs`), and whether a file or a catalog passes Authenticode with the signer's pin (today in `src/Thermi/Commands/WindowsInstaller.cs`).

**The service** gains `src/Thermi.SensorHost/Updates`: the check and download (`ManifestClient.cs`), the unpacking and its checks (`Unpacker.cs`), the switch and the watchdog (`Switcher.cs`, `thermi-sensors --switch`, hidden, which holds `update\switch.lock` while it acts), the services' image paths (`ServiceConfig.cs`), and the updater's loop (`Updater.cs`), with code 128 in `SensorService.OnCustomCommand`. Its events go to the event log as today. The service's access list gains user-defined control for interactive users.

**The overlay** gains `src/Thermi/Updates`: it reads `state.json` when its tick sees the file changed. While the file names a version ready, it sends code 128 at its quiet moment; once the installed version is not its own, it switches at its quiet moment: it closes as its tray's "Exit thermi" does, releases `Local\thermi-overlay`, starts the fixed `thermi.exe`, and exits once a `thermi-host` window of that process shows; past 60 ticks it ends that process, takes the mutex back, runs its overlay again and leaves that version alone for the rest of its run. Its quiet moment is decided in Core from the window's mutex, the drag, `GetLastInputInfo`, the session's lock and the overlays' visibility.

**The window** shows the update line in About from `UpdateStatus`, through the bridge's `About`.

**The levers** are `src/Thermi.Releases`, a console tool that `scripts/updates.ps1` runs: `keys`, `pack`, `publish`, `promote`, `rollback`, `disable`, `refresh`, `status` and `serve`, over a folder or the R2 bucket through its S3 API, signing with a private key from a file or `THERMI_UPDATES_KEY`. `serve` answers a folder over HTTP on this PC, for a test build. The tool and the service share Core's manifest code; nothing of the tool ships.

**Settings.** `src/Thermi/Overlay/ConfigStore.cs` already keeps the file of an earlier format beside it when it writes the new one, as `config.v<N>.json`; it gains the way back of R19.

**Builds.** `ThermiUpdatesFrom` and its keys, in `Directory.Build.props`, are empty by default, and a build without them never checks. The release workflow builds with the real manifest address and both public keys, kept as the repository's variables, with the catalog required; `scripts/install.ps1 -UpdatesFrom <url> -UpdatesKeys <folder>` builds a test install that trusts the keys in that folder, without the catalog.

**The MSI** is `Thermi.Installer`, a WiX project of its own built by `dotnet build`. It installs a bootstrap it owns, the version it ships, and runs that version's `thermi-sensors.exe --adopt`, which lays down the layout an update keeps: the version's folder, the launchers, both services with today's settings (recovery, privileges, the young generation, the event source, the access lists) and `update\`; the MSI itself adds the Start menu entry and the Active Setup entry that turns on the start at sign-in once per profile. Uninstalling runs `thermi-sensors.exe --remove`, which stops and deletes both services and removes every version, and the MSI removes the rest of `C:\Program Files\thermi`. The updater writes the version Installed apps shows. The release workflow builds and signs it.

**Files that change**, besides those above: `scripts/install.ps1` (the layout, the launchers, the test switches), `Directory.Build.props`, `.github/workflows/ci.yml` (the MSIs on every PR) and a new release workflow, `src/Thermi/Program.cs`, `src/Thermi.Core/Bridge/WindowFacts.cs` (About's line), the translations, `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/QUICKSTART.md`, the READMEs, and a privacy note.

**Rejected:** an MSI for every update, as this spec first had it: thermi would vanish for seconds at each update, and the service would need back the privilege to start programs in users' sessions. The others are in the ADR.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
