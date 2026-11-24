# 004. thermi keeps itself up to date, silently and safely

**Status**: draft

## Why

Today thermi is built and installed from the repo by `scripts/install.ps1`. A public release needs an installer people trust, and fixes that reach every PC without a UAC prompt, a visit to a website or a gap in the overlay, as Chrome updates itself; and its owner needs Mi Basket's levers to stage a release, stop it and take it back within a minute. The service that installs runs as LocalSystem, so it must be impossible to make it run anything but a release signed by thermi's author.

The decisions, the threat model and the alternatives are in `docs/adr/0008-updates.md`.

## Requirements

Getting thermi

- R1. Each release is one MSI per processor, x64 and Arm64, that installs thermi for the whole PC: the two launchers, one version's folder, both services, the Start menu entry and the start at sign-in of whoever installs it, with no other download. Uninstalling removes every version and leaves each user's settings.
- R2. In a release, every executable and library thermi installs or unpacks carries a valid Authenticode signature: thermi's, or its maker's.
- R3. Installing over a version, an update and a rollback each keep every user's settings, saved styles, log, "Start with Windows" and the tray icons they chose to show.
- R4. `scripts/install.ps1` installs the same layout as the MSI. Its builds never check for updates unless it is given a manifest's address and public key, and the window says that this build does not update itself.

Checking

- R5. The service checks a minute after it starts, every 4 hours, and after Windows resumes when its last check is 4 hours old or more.
- R6. A check is one HTTPS GET of the channel's fixed address, with no query, no cookie, no identifier and `thermi` as the whole User-Agent.
- R7. Under `HKLM\SOFTWARE\Policies\thermi`, `UpdateChannel` = `beta` follows the beta channel, anything else the stable one, and `Updates` = 0 turns checking off.
- R8. On a metered network the service checks, but downloads only a version marked urgent.

Trust

- R9. The service takes a manifest only when its envelope is at most 64 KB and signed by a key built into thermi, it names the PC's channel, and its sequence is not below the last one taken.
- R10. A zip is unpacked only when the file the service holds open has the size and SHA-256 the manifest gives; in a release build, every executable and library it unpacks must pass Authenticode, thermi's own pinned to its signer, before the version can run. Any refusal leaves everything installed as it was, and the event log says which check failed.
- R11. The service never takes a version at or below the installed one, nor one that failed on this PC. When the manifest withdraws the installed version, the PC goes back to the previous version it kept, and stays where it is without one.
- R12. Every entry of a zip lands inside its version's new folder. The update reads and writes only under `C:\Program Files\thermi`, which it checks before each use to be a real folder that only administrators, SYSTEM and Windows' installer may change; a check that fails changes nothing and is logged.
- R13. The PC draws a number from 0 to 99 once and keeps it, and takes a version only when that number is below the manifest's rollout percentage.

Switching

- R14. A ready version switches when the service starts, when an overlay says its session is quiet (no settings window and no drag, and three minutes without input, the session locked or every overlay hidden), or at once when the manifest marks it urgent. Nothing asks for administrator rights.
- R15. The switch replaces only the two launchers in `C:\Program Files\thermi`; a process that runs keeps its version until it switches.
- R16. Once the service switched, each overlay switches at its own quiet moment, or at once for an urgent version unless its window is open or a drag is under way: the new version shows the same overlays before the old one exits, so the overlays never blink.
- R17. The new service passes when it serves a snapshot within 120 polls of its pipe, one a second, and fails when it does not, or stops with an error twice within 10 minutes of its switch. A new overlay passes when it shows its overlays within 60 of the old overlay's ticks, and fails when it exits first or never shows them. A failure on the PC or in any session takes the whole PC back to the previous version and marks the failed one, never to be taken again. Time Windows spends asleep never counts.
- R18. The service keeps the installed version and the previous one, and deletes every other version's folder that no process runs from.
- R19. A version that finds the settings in a newer format than it reads, with the copy it kept of them in its own format beside them, reads that copy and keeps the newer file beside it.
- R20. A service serves the pipe of the release before it: the lines it writes for a recording are held to a golden file, which a change of format must replace on purpose.

The owner

- R21. `scripts/updates.ps1` publishes a build to a channel with a rollout and an urgent flag, promotes beta's version to stable, rolls a channel back, turns it off and shows every channel; each is a new manifest signed with the next sequence. `publish` and `promote` refuse a version below the channel's, and a version reaching stable reaches beta too when beta is behind. A merge to main publishes to beta, once the repo holds the key.
- R22. The manifests are served with a 60-second cache and the zips as immutable, so a lever reaches a PC at its next check.

The user

- R23. About shows the version, and one line on updates: up to date and when it last checked, a version ready and that it switches when the PC is quiet, updates turned off by the PC's policy, the beta channel, no successful check for 14 days, or a build that does not update itself. Nothing else ever shows an update.
- R24. Each check that finds a version, each download, switch, rollback and refusal goes to the event log under `thermi-sensors`, and the service stays under the 80 MB of `docs/adr/0011-sensor-service-memory-budget.md`.

## Design

**Layout.** `C:\Program Files\thermi` holds `thermi.exe` and `thermi-sensors.exe`, .NET app hosts that the build writes to start `<version>\thermi.dll` and `<version>\thermi-sensors.dll`, beside a folder per kept version and `update\` with the service's `state.json` and its downloads. Each version's folder is a self-contained publish of both programs and carries its own launchers in `<version>\launchers\`, which the switch copies to the top. The build makes them with the SDK's own `CreateAppHost` task, from the same app host and resources it gives the program, in `src/Thermi/Launcher.targets`, which both programs import. Everything that names thermi's path, the Run value, the services, the Start menu and the tray icons' registration, names the fixed one; `src/Thermi/Ui/StartWithWindows.cs` writes the launcher's path rather than its own process's.

**Core** gains `src/Thermi.Core/Updates`: the envelope and the manifest and their checks (`Manifest.cs`), versions and their order (`ReleaseVersion.cs`), what the service takes from a manifest given its state (`UpdatePlan.cs`), when to check and when to switch (`UpdateSchedule.cs`), the health of a switch by counted polls, ticks and errors (`Probation.cs`), the state file (`UpdateState.cs`), the levers on a channel (`Levers.cs`), and the line About shows (`UpdateStatus.cs`). Each is pure and table-tested.

**Trust on Windows** moves to a library of its own, `src/Thermi.Trust`, since the driver's install and the updater now both need it: whether a folder is one only administrators change (today in `src/Thermi/Commands/DriverFolder.cs`) and whether a file passes Authenticode (today in `src/Thermi/Commands/WindowsInstaller.cs`), with the signer's pin.

**The service** gains `src/Thermi.SensorHost/Updates`: the check and download (`ManifestClient.cs`), the unpacking (`Unpacker.cs`), the switch and the watchdog (`Switcher.cs`, `thermi-sensors --switch <from> <to>`, hidden), the control codes (`SensorService.OnCustomCommand`), and the updater's loop (`Updater.cs`). Its events go to the event log as today. The service's access list gains user-defined control for interactive users.

**The overlay** gains `src/Thermi/Updates`: it reads `state.json` when its tick sees the file changed. While the file names a version ready, it sends code 128 at its quiet moment; once the installed version is not its own, it hands over to it at its quiet moment: it releases `Local\thermi-overlay`, starts the fixed `thermi.exe` with `--taking-over <pid>`, and exits when the new one posts it `thermi-took-over` after showing its overlays; it counts its ticks meanwhile, and on a failure kills the new one, takes the mutex back and sends code 129. Its quiet moment is decided in Core from the window's mutex, the drag, `GetLastInputInfo`, the session's lock and the overlays' visibility.

**The window** shows the update line in About from `UpdateStatus`, through the bridge's `About`.

**The levers** are `src/Thermi.Releases`, a console tool that `scripts/updates.ps1` runs: `keys`, `pack`, `publish`, `promote`, `rollback`, `disable`, `status` and `serve`, over a folder or the R2 bucket through its S3 API, signing with a private key from a file or `THERMI_UPDATES_KEY`. `serve` answers a folder over HTTP on localhost, for a test build. The tool and Core share the manifest's code; nothing of it ships.

**Settings.** `src/Thermi/Overlay/ConfigStore.cs` already keeps the file of an earlier format beside it when it writes the new one, as `config.v<N>.json`; it gains the way back of R19.

**Builds.** `ThermiUpdates` in `Directory.Build.props` is `Off` by default. The release workflow builds with the real manifest address and both public keys from `src/Thermi.SensorHost/Updates/keys`, and Authenticode required; `scripts/install.ps1 -UpdatesFrom <url> -UpdatesKey <pem>` builds a test install that trusts that key, with Authenticode not required.

**The MSI** is `src/Thermi.Installer`, a WiX project built by `dotnet build`, which lays down the launchers, the first version's folder, both services with today's settings (recovery, privileges, the young generation, the event source, the access lists), `update\`, the Start menu entry and the policy-free defaults, and on uninstall removes `C:\Program Files\thermi` whole. The release workflow builds and signs it.

**Files that change**, besides those above: `scripts/install.ps1` (the layout, the launchers, the test switches), `Directory.Build.props`, `.github/workflows/ci.yml` (the MSIs on every PR) and a new `.github/workflows/release.yml`, `src/Thermi/Tray/ThermiTray.cs` (the icons' ids from the fixed path), `src/Thermi/Program.cs` (`--taking-over`), `src/Thermi.Core/Bridge/WindowFacts.cs` (About's line), the translations, `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/QUICKSTART.md`, the READMEs, and a privacy note.

**Rejected:** an MSI for every update, as this spec first had it: thermi would vanish for seconds at each update, and the service would need back the privilege to start programs in users' sessions. The others are in the ADR.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
