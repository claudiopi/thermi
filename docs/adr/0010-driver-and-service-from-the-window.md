# 0010. The window installs the driver and starts the sensor service

**Status**: Accepted

## Context

Spec 002 lets thermi fix the two things that most often stop its sensors (`docs/specs/002-settings-window/spec.md`): the `thermi-sensors` service stopped (R45, also "Start sensors" in thermi's menu), and the driver missing, seen as a CPU temperature, power or clock of exactly zero (R145). Each needs administrator rights, behind one UAC prompt. The window and the overlay run as the user and stay that way (`docs/adr/0003-sensor-service.md`). The driver is PawnIO, which thermi never bundles and installs only with its official installer (`docs/adr/0002-sensors.md`). Its name means nothing to most people, and in thermi's own words it would read as if thermi made it or vouched for it, so no text of thermi names it (R145).

## Decision

- **One elevated mode.** `thermi elevated start-sensors` and `thermi elevated install-driver` are modes of the same `thermi.exe`, under Program Files. The window, or the overlay for "Start sensors", starts one with `ShellExecuteEx` and the `runas` verb: one UAC prompt, naming thermi. The mode takes its verb and nothing else; no path, URL, hash or setting comes from the caller, so whoever starts it cannot steer it. It shows no window, loads no WebView2, reads no settings and exits when done. Its exit code is the result, which the caller reads from the process handle, off its UI thread.
- **Start sensors** starts `thermi-sensors` through the service control manager and waits up to 10 s for it to run. The window still recovers by seeing the pipe come back (R45). A missing service has its own exit code, and the window says to reinstall thermi.
- **Install driver.** thermi's build holds one URL of the driver's official installer, on its project's own release page, with its size and SHA-256. The mode downloads it into `%ProgramData%\thermi\driver`, which only administrators and SYSTEM can write, checked for owner, access and junctions before each use, as `docs/adr/0008-updates.md` does. It checks the size, then the SHA-256, then a valid Authenticode signature; trust rests on the hash, never on TLS. It keeps the file open, shared only for reading, runs it with the installer's own unattended switch, waits up to 2 minutes, deletes it, and restarts `thermi-sensors` so LibreHardwareMonitor opens the driver. It posts its phase (downloading, installing, starting the sensors) to the window's beacon as a registered message; an elevated process may post to the user's windows, never the reverse. The window says the driver is installed only once a CPU temperature reads.
- **Never redistributed.** thermi never ships, mirrors or keeps the installer. It comes from the driver's own release at each install, as if the person had downloaded it. Once installed, the driver is the person's, in Installed apps under its own name; uninstalling thermi leaves it, since other programs use it too.
- **Never named.** The product's name lives only in the mode's constants and in docs. The UAC prompt names thermi, the program elevated, and the installer runs unattended, so its own windows never show. A check in `RepoRulesTests` fails on any translation that holds the name; the texts that hold it today change with spec 002.
- A refused prompt starts nothing, so the window can say that nothing changed (R145).

## Rejected

- Running the driver's installer itself with `runas`: the prompt and the installer's windows name the product, the file waits in a folder the user can write until the prompt, and nothing restarts the service after.
- Letting interactive users start `thermi-sensors`, through its security descriptor: no prompt at all, but any user could start a service an administrator stopped on purpose, and spec 002 asks for the prompt.
- The service installing the driver, through ADR 0008's control codes: no prompt, so any user could add a kernel driver without an administrator's consent. And a stopped service, the other fix, cannot start itself.
- Shipping the installer in thermi's MSI: redistribution, and the driver's version would ride on thermi's releases.
- `winget`: missing from some editions of Windows, it shows its own window, and trust would rest on a community catalog instead of a hash in thermi's code.

## Consequences

- A PC missing both fixes can see two prompts, one each. For an administrator each is one click; a standard user needs an administrator's password, and the download then uses that administrator's proxy settings.
- A new version of the driver is a thermi PR that changes the URL, size and hash together, after a person checks the release. If the pinned file leaves the release page, the install fails and says so; watch it.
- Unsigned developer builds show "Unknown publisher" in the prompt; signed ones show thermi.
- Anti-cheat software may refuse to run beside the driver (ADR 0002); "What is this?" says so before anyone installs it.
- The mode's steps sit behind interfaces for the service manager, the download and the installer's process, so tests cover a refused prompt, a missing service, a timeout, a wrong hash and a bad signature. The real install is checked by hand, on a PC without the driver.

## What it replaces in earlier ADRs

- `docs/adr/0002-sensors.md` says thermi detects whether PawnIO is installed and offers the official installer. Instead, a missing driver is inferred from zeros (spec 002), and thermi runs the official installer itself, unattended, behind one UAC prompt, from a URL and hash it pins.
- `docs/adr/0003-sensor-service.md` says the overlay never needs admin rights. It still never runs with them, but the window and the overlay may ask Windows for them, one fix at a time, for thermi's elevated mode.
- `docs/adr/0008-updates.md` says the update is the only download, and bans network APIs outside the updater. Instead, the elevated driver install downloads one pinned file too, and the rule allows it there.
