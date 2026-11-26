# 004. thermi keeps itself up to date, silently and safely

**Status**: done

**Budget**: 154 .NET and 0 page cases

thermi installs from a signed MSI, for the whole PC or, with no administrator rights, for one user, and then updates itself the way Chrome does: no prompt, no website, no gap in the overlay. The sensor service checks a signed manifest, downloads the new version beside the running one and switches when the PC is quiet, going back by itself when the new version fails. The owner has levers to stage a release, stop it and take it back within a minute. The service runs as LocalSystem, so it can never be made to run anything but a release signed by thermi's author.

The decisions, the threat model and the alternatives are in `docs/adr/0008-updates.md`; installing without administrator rights is in `docs/adr/0016-admin-on-demand.md`.

## Requirements

### Getting thermi

- R1. Each release has one machine MSI per processor, x64 and Arm64, that installs thermi for the whole PC with no other download: the launchers, one version's folder, both services and the Start menu entry, and it turns on the start at sign-in once in each profile. Uninstalling removes every version and leaves each user's settings.
  Release check: Install the release's MSI.
  Hardware report: Arm64.
- R2. In a release, every file thermi installs or unpacks is listed in a Windows catalog signed by thermi's Authenticode identity, and every executable thermi builds carries that signature too.
  Release check: Install the release's MSI.
- R3. Installing over a version, an update and a rollback each keep every user's settings, saved styles, log, "Start with Windows" and the tray icons they chose to show; only a rollback across a change of the settings' format returns to the settings as they were before it (R19).
  Release check: Install over the previous release.
- R4. `scripts/install.ps1` installs the same layout as the MSI. Its builds never check for updates unless it is given a manifest's address and public key, and the window says that this build does not update itself.
- R34. Neither MSI shows a window for a program it runs, so a repair a standard user starts with `msiexec /fa` shows no console of a program running as SYSTEM, and still puts back the services and the launchers. Run by hand with its full UI, an MSI has Explorer start the overlay unelevated, as the person, even from an administrator's elevated console. Where no shell runs, the overlay may be started with the elevated client's token; then the overlay and the settings window refuse to run with an administrator's token that UAC raised, say why on the terminal and exit before any window, writing no file, and the overlay starts at the next sign-in.

### Checking

| When | What |
| --- | --- |
| A minute after the service starts | a check |
| Every 4 hours | a check |
| After Windows resumes, with the last check 4 hours old or more | a check |
| When About opens | a check, at most once a minute (R25) |

- R5. The service checks a minute after it starts, every 4 hours, and after Windows resumes when its last check is 4 hours old or more.
- R6. A check is one HTTPS GET of the channel's fixed address, with no query, no cookie, no identifier and `thermi` as the whole User-Agent.
- R7. Under `HKLM\SOFTWARE\Policies\thermi`, `UpdateChannel` = `beta` follows the beta channel, anything else the stable one, and `Updates` = 0 turns checking off.
- R8. On a metered network the service checks, but downloads only a version marked urgent; it downloads nothing without three times the zip's size free.

### Trust

- R9. The service takes a manifest only when its envelope is at most 64 KB and signed by a key built into thermi, it names the PC's channel, its sequence is not below the last one taken, and it has not expired; a manifest expires at most 45 days after it was signed. One signed by the backup key may revoke the release key it names by SHA-256 and set the sequence back; one that expires further ahead than 45 days and a day is refused.
- R10. A zip is unpacked only when the file the service holds open has the size and SHA-256 the manifest gives, and a version runs only when the versions inside it are the manifest's and, in a release build, the signed catalog lists every file it holds. Any refusal leaves everything installed as it was, and the event log says which check failed.
- R11. The service never takes a version at or below the installed one, nor one that failed on this PC. When the manifest withdraws the installed version, the PC goes back to the previous version it kept, unless that one failed too, and stays where it is without one.
- R12. Every entry of a zip lands inside its version's new folder. The update reads and writes only under `C:\Program Files\thermi`, which it checks before each use to be a real folder that only administrators, SYSTEM and Windows' installer may change; a check that fails changes nothing and is logged.
- R13. The PC draws a number from 0 to 99 once and keeps it, and takes a version only when that number is below the manifest's rollout percentage.

### Switching

- R14. A ready version switches when the service starts, when an overlay says its session is quiet (no settings window and no drag, and either three minutes without input, the session locked or every overlay hidden), or at once when the manifest marks it urgent. Nothing asks for administrator rights, and users can send the service no other command.
- R15. A switch changes only the services' image paths and the two launchers in `C:\Program Files\thermi`; a process that runs keeps its version until it switches. A power cut or a crash at any point of a switch leaves Windows starting the old version or the new one whole, and the service finishes or undoes the switch at its next start.
- R16. Once the service switched, each overlay switches at its own quiet moment, or for an urgent version as soon as its window is closed and no drag is under way, by starting the fixed launcher once it is that version's. When the new overlay does not come up within 60 of the old one's ticks, the old one comes back and leaves that version alone in its session.
- R17. The new service passes when it serves a snapshot within 120 polls of its pipe, one a second, and fails when it does not, or when a second run of it ends without stopping within 10 minutes of its switch. A failure takes the PC back to the previous version and marks the failed one, never to be taken again. Sleep never fails a version, nor can any user through thermi; a user who floods its pipe through a probation can fail that one version on that PC (`docs/adr/0008-updates.md`).
- R18. The service keeps the installed version, the previous one, the one ready and the one the launcher at the top still starts, and deletes every other version's folder once Windows lets it rename that folder aside. It writes its state whole or not at all.
- R19. A version that finds the settings in a newer format than it reads, with the copy it kept of them in its own format beside them, reads that copy and keeps the newer file beside it.
- R35. A version keeps what a later one added to the settings without changing their format, in any object of the file, through every edit and undo it writes, so the later version finds it again after a rollback.
- R20. A service serves the pipe of the release before it: the lines it writes for a recording are held to a golden file, which a change of format must replace on purpose.

### The owner's levers

- R21. `scripts/updates.ps1` publishes a build to a channel with a rollout and an urgent flag, promotes beta's version to stable, rolls a channel back, turns it off, signs it again before it expires, revokes the release key with the backup key, and shows every channel; each is a new manifest signed with the next sequence. `publish` and `promote` refuse a version below the channel's, and a version reaching stable reaches beta too when beta is behind. A merge to main publishes to beta, and each publish waits for the owner's approval.
- R22. The manifests are served with a 60-second cache and the zips as immutable, so a lever reaches a PC at its next check.
  Release check: The update server's cache.

### What the user sees

- R23. About shows the version, and one line on updates: up to date and when it last checked, a version ready and that it switches when the PC is quiet, updates turned off by the PC's policy, the beta channel, no successful check for 14 days, or a build that does not update itself. Nothing else ever shows an update.
- R24. Each check that finds a version, each download, switch, rollback and refusal goes to the event log under `thermi-sensors`, and the service stays under the 80 MB of `docs/adr/0011-sensor-service-memory-budget.md`.
- R25. Opening About asks the service for a check right then, as Chrome's About page does: its line says it checks until the service has, half a minute at most, then what it found, and up to date says checked just now for a minute. About and the service each heed the ask at most once a minute, so reopening About never hammers the channel. A version ready whose switch is not held offers Restart to update, which switches as soon as a check under way ends, and closes the window.

### Without administrator rights

- R26. Each release also ships a per-user MSI per processor, with an UpgradeCode of its own, which installs without a UAC prompt into `%LOCALAPPDATA%\Programs\thermi`, in the same layout, through impersonated `--adopt --user`; it starts the overlay at the user's sign-in through their Run key and registers no service, no event source and no driver.
  Release check: A standard user's install.
- R27. With no `thermi-sensors` service installed, the overlay starts `thermi-sensors --user` from the top launcher: a hidden process of the user's that reads what the user's rights allow and serves the same lines on the pipe `thermi-sensors-<user SID>`, created as its first instance, owned by the user's SID, open only to that user, SYSTEM and administrators, and closed to remote clients. It ends 10 s after its last client leaves, with the Stopped mark, which it writes too when a sign-out or a restart ends it; waits 30 s for a first client; stays while an install or a switch runs; refuses to run when one of its named events already exists; and logs to `%LOCALAPPDATA%\thermi\thermi-sensors.log`. A host that ended is started again at the overlay's next try; after one that ended within 10 s of its start, the wait doubles up to a minute. No host is started while `update\switch.lock` is held.
- R28. The overlay reads the service's pipe whenever the service is installed, and the user's pipe only when its owner is the user's SID, SYSTEM or administrators, its server process runs from the user's install folder at an integrity level no lower than the overlay's, and the pipe's own label is no lower than the overlay's, or medium for an overlay above it.
- R29. In a per-user install, while it reads the user's own host, "Double-click to fix" on a "–" the driver or admin rights would fill, and the window's fix for them, offer to install thermi for everyone. The user's host then:
  - downloads the machine MSI of the version the channel offers, when it is the running one or later, with the URL, size and SHA-256 the signed manifest gives;
  - checks it, and in a release build that thermi's certificate signed it, and holds it open, shared only for reading;
  - holds the updater's lock so no check or switch begins meanwhile;
  - runs `msiexec.exe /i <msi> ADDDRIVER=1 /passive` without `runas`, so the one UAC prompt names thermi's signer, after the overlay or window let it take the foreground.

  A refused prompt, a failed download or a wrong hash changes nothing and says so; a channel turned off, a withdrawn running version or the policy `Updates` = 0 opens thermi's download page instead. Once the install worked, the user's host ends, and the window says so only once its readings come from the service.
- R30. The machine MSI's `ADDDRIVER`, a secure property, acts only on a first install (`NOT Installed`): it starts `thermi-elevated` with `install-driver`, which installs nothing over the pinned driver or a later one already there, and then only starts the sensors again. The MSI ignores the result, so a driver that fails or needs a restart leaves thermi installed, the event log says why, and the window keeps offering its fix.
- R31. Once the service serves, the per-user overlay points the user's Run value, if there is one, at the machine's launcher, marks the machine's Active Setup done in this profile, starts the machine's overlay and exits. The machine's overlay then removes the per-user product, found by its own UpgradeCode in the user's context, once nothing runs from its folder; its removal deletes the Run value only while it still names the per-user launcher. The settings, saved styles and log stay.
  Release check: A standard user's install.
- R32. A per-user install updates itself as the service does, with the same checks, the machine's policy, levers, holds and failures, its rollout number and failures the user's own. Its watchdog, run as the user under `update\switch.lock`, ends the old host, starts the new version's host directly, and places its launchers only once it serves within 120 polls, else starts the old one again; a cut at any step leaves the launchers on a version that served. The overlay's quiet moment and About's check and restart reach the host as named events in the user's session.
- R33. A version's entry in the manifest lists its machine MSI for each processor, with its URL, size and SHA-256, and `scripts/updates.ps1 pack --msi` builds both MSIs.

## Design

| Where | What |
| --- | --- |
| `src/Thermi.Core/Updates` | Pure and table-tested: versions and their order (`ReleaseVersion.cs`); the envelope, the manifest and their checks, a download's size and hash among them (`Manifest.cs`); what the service takes from a manifest (`UpdatePlan.cs`); the policy, when to check and the quiet moment (`UpdateSchedule.cs`); a switch's health by counted polls, ticks and starts (`SwitchHealth.cs`); the state file with its switch written down (`UpdateState.cs`); the levers (`Levers.cs`); which zip entries land inside their folder (`ZipEntries.cs`); About's line (`UpdateStatus.cs`). |
| `src/Thermi.Core/Install` | The install folder's layout and which version a service runs (`InstallLayout.cs`), which MSI "install for everyone" runs (`InstallForEveryone.cs`), whether the driver installed is the pinned one or later (`DriverVersion.cs`), and the user's host's choices (`UserHost.cs`). |
| `src/Thermi.Trust` | Windows' own checks, shared by the driver's install and the updater: a folder only administrators change (`AdminFolder.cs`), Authenticode with the signer's pin (`Authenticode.cs`), signed catalogs (`FileCatalog.cs`) and a held lock (`LockFile.cs`). |
| `src/Thermi.Sensors.Windows/Updates` | The check and download (`ManifestClient.cs`), unpacking and its checks (`Unpacker.cs`), the switch and its watchdog (`Switcher.cs`, run as `thermi-sensors --switch` while it holds `update\switch.lock`), the services' image paths (`ServiceConfig.cs`) and the updater's loop (`Updater.cs`). The service's access list lets interactive users send codes 128 (quiet), 129 (check) and 130 (restart), heard in `SensorService.OnCustomCommand`. |
| `src/Thermi.Sensors.Windows/Install`, `User` | `--adopt`, `--undo`, `--forget` and `--remove`, which the MSIs and `scripts/install.ps1` run; the `--user` host with its pipe, file log, named events and the download and run of the machine MSI (`MachineInstaller.cs`). |
| `src/Thermi.Windows/Updates` | The overlay reads the highest numbered copy of the state, `update\status-<n>.json`, sends code 128 at its quiet moment, and hands over to the new version through the fixed `thermi.exe`; the window's `WindowUpdates` sends 129 when About opens and 130 for Restart to update. |
| `src/Thermi.Releases` | The owner's levers, which `scripts/updates.ps1` runs: `keys`, `pack`, `publish`, `promote`, `rollback`, `disable`, `refresh`, `revoke`, `unrevoke`, `status` and `serve`, over a folder or the R2 bucket. It shares Core's manifest code; nothing of it ships. |
| `src/Thermi.Installer` | The two MSIs, machine and per-user, built with WiX 5 from a published version. Every program they run goes through WiX's util extension: `WixQuietExec`, and `WixUnelevatedShellExec` for the overlay. |

**Layout.** `C:\Program Files\thermi` holds `thermi.exe` and `thermi-sensors.exe`, .NET app hosts that start `<version>\thermi.dll` and `<version>\thermi-sensors.dll`; a folder per kept version, each a self-contained publish of both programs with its own launchers in `<version>\launchers\`; and `update\`, with the service's `state.json`, its downloads and the files it renamed aside. The services' image paths name `<version>\thermi-sensors.exe`. The launchers come from the SDK's own `CreateAppHost` task in `src/Launchers.targets`. `src/Thermi.Windows/Ui/StartWithWindows.cs` writes the launcher's path, and the tray icons' ids come from it, so they survive an update.

**Builds.** `ThermiUpdatesFrom` and its keys in `Directory.Build.props` are empty by default, and a build without them never checks. The release workflow, `.github/workflows/release.yml`, builds with the real manifest address and both public keys, with the catalog required; `scripts/install.ps1 -UpdatesFrom <url> -UpdatesKeys <folder>` builds a test install that trusts the keys in that folder, without the catalog.

**Settings.** `src/Thermi.App/SettingsFile/ConfigStore.cs` keeps the file of an earlier format beside the new one as `config.v<N>.json`, and reads it back after a rollback (R19).

**Rejected:** an MSI for every update. thermi would vanish for seconds at each update, and the service would need the privilege to start programs in users' sessions. The other alternatives are in the ADR.

## Acceptance

The steps are in `acceptance.json`; what stays open is in `tasks.md`.
