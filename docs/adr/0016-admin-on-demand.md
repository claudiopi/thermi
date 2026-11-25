# 0016. thermi installs and runs without administrator rights; one prompt adds what needs them

**Status**: Proposed

## Context

thermi needs administrator rights today to install (`docs/adr/0003-sensor-service.md`: a LocalSystem service under Program Files) and once more for the driver (`docs/adr/0010-driver-and-service-from-the-window.md`). Many people, on a work PC or as a standard user, cannot or will not say yes to that before they have seen anything, and competitors sell "no admin needed". Most of what thermi shows needs no rights at all. A dump without them on this PC (`tests/Thermi.Tests/Recordings/ryzen-9950x-rtx-5060-ti-no-admin.json`) reads the CPU's load, both GPUs' temperatures, power, clocks and loads, and memory. Missing are the CPU's temperature, power and clocks, the board's fans and the drives' SMART, which need the Administrators group and, for the CPU, the driver. The 002 overlay already shows those as "–" with "Double-click to fix".

## Decision

**Two scopes of one version.** Each release ships two MSIs per processor from one WiX source:

- `thermi-<v>-<cpu>.msi` is per machine, as now.
- `thermi-<v>-<cpu>-user.msi` is per user, with an UpgradeCode of its own. It installs without a prompt into `%LOCALAPPDATA%\Programs\thermi`, in the same layout: `<v>\`, launchers at the top, `update\`. Its actions run impersonated as `thermi-sensors --adopt --user` and `--remove --user`. It registers no service, no event source and no driver, and writes the user's Run key as `--adopt` finds it.

winget offers both, by its `Scope`. AppLocker's default rules and the `DisableUserInstalls` policy block per-user installs; such PCs use the machine MSI.

**The sensors as the user.** Without the `thermi-sensors` service, the overlay starts `thermi-sensors --user` from the top launcher.

- It is a hidden process of the user's, with the same host and LibreHardwareMonitor, reading what the user's rights allow.
- It serves the same lines on the pipe `thermi-sensors-<user SID>`. It creates the pipe as the first instance and rejects remote clients, as ADR 0003's pipe does. It sets the user's SID as the pipe's owner, and an access list that lets only that user, SYSTEM and administrators open it.
- It runs only while a client is connected and ends 10 s after the last one leaves, writing the Stopped mark first, as the service's stop does; the 10 s let a switch's watchdog read the new host and leave before the overlay comes back to it. A host that has had no client yet waits 30 s for its first. A host running an install or a switch stays until that ends. A run that ends without the mark counts as a crash, by ADR 0008's rule.
- It logs to `%LOCALAPPDATA%\thermi\thermi-sensors.log`, since only an administrator can register an event source.

The overlay reads the service's pipe whenever the service is installed, and the user's pipe only when the service is not. It trusts the user's pipe only if three things hold:

- its owner is the user's SID, or SYSTEM or administrators, as today;
- the server process, from `GetNamedPipeServerProcessId`, runs at an integrity level no lower than the overlay's;
- that process runs from the user's install folder.

So neither another user nor a sandboxed process of the same user who takes the name first is ever read. A host that ends is started again at the overlay's next try, 2 s later, and after a host that ended within 10 s of its start the wait doubles, up to a minute, so a host that cannot start never becomes a loop. While `update\switch.lock` is held, the overlay starts no host.

**One prompt adds the rest.** In a per-user install, "Double-click to fix" on a "–" and the window's fix offer **Install for everyone**. The user's host then does the work:

- It reads the channel's manifest and takes the machine MSI of the version it offers, when that version is the running one or later. Each version's entry lists its machine MSIs' URLs, sizes and SHA-256s.
- It downloads the MSI into its own folder, checks size and hash, and keeps the file open, shared only for reading, until `msiexec` exits, as ADR 0010 does with the driver's installer.
- It runs `msiexec.exe /i <msi> ADDDRIVER=1 /passive` without `runas`. The per-machine package asks Windows Installer to elevate, and its UAC prompt names the package's signer, thermi. The overlay or window that asked first calls `AllowSetForegroundWindow` for the host, so the prompt comes to the front rather than flashing on the taskbar.
- `/passive` keeps the UI level below the one at which the MSI's `StartOverlay` runs, so no .NET program of thermi's ever starts elevated with the user's environment.
- A refused prompt (1223), a failed download or a wrong hash changes nothing, and the window says so. A channel turned off, a running version withdrawn, or the policy `Updates` = 0 has no MSI to offer. The fix then opens thermi's download page instead.

The machine MSI's `ADDDRIVER` is a secure property, so it reaches the elevated part when a standard user starts `msiexec` and an administrator consents. Its action runs only on a first install (`NOT Installed AND ADDDRIVER = 1`), which always needs that consent, while a repair sets `Installed`; and only when the driver is missing. A test reads the condition and the secure flag from `Package.wxs`. It starts the `thermi-elevated` service with `install-driver`, so the driver comes through ADR 0010's path, as SYSTEM with the machine's environment, never from the action itself. It returns `ignore`: a driver that fails or needs a restart (3010) leaves thermi installed and writes why to the event log, and the window keeps offering its fix.

**The move to the machine install** happens in this order:

1. The per-user overlay sees the service serve. It writes the user's Run value to the machine's launcher only if the value existed, so a start at sign-in that was turned off stays off. It marks the machine's Active Setup as done in this profile, so Active Setup never turns that start back on.
2. It starts `C:\Program Files\thermi\thermi.exe` and exits once that overlay is up, as ADR 0008's handover does. The per-user host ends 10 s after its last client leaves.
3. The machine overlay, finding a per-user product of this user, looks it up by the per-user UpgradeCode in the user's unmanaged context only. It removes it with `msiexec /x <product> /qn` once no process runs from the per-user folder. That removal needs no rights. Its `--remove --user` deletes the user's Run value only while it still names the per-user launcher.

The settings, saved styles and log, in `%APPDATA%\thermi` and `%LOCALAPPDATA%\thermi`, are shared by both scopes and stay.

**Updates in both scopes.** The service's updater is unchanged. In a per-user install the same `Updater` runs in the user's host, with `InstallFolder` rooted in the user's folder:

- It checks, downloads and verifies exactly as the service does: the manifest's signature, sequence and expiry, the hashes, and in a release build the signed catalog.
- It honors the machine's policy under `HKLM\SOFTWARE\Policies\thermi`.
- Its rollout number and its record of failed versions belong to the user, in the user's `update\`.
- The trusted-folder check becomes "a folder only this user, SYSTEM and administrators write". Anyone who can write it already runs as the user.

The per-user switch runs through the watchdog, `thermi-sensors --switch <v> --user`, holding `update\switch.lock`:

1. It writes the switch down.
2. It ends the old host through its stop event.
3. It starts `<new>\thermi-sensors.exe --user` directly and waits up to 120 polls for it to serve.
4. Only then does it place the new version's launchers. A new version that never serves is ended, and the old one is started again.

A cut at any step leaves the launchers on a version that served, and the switch written down is finished or undone at the host's next start, as the service's is. The overlay hands over as it does now.

ADR 0008's codes, the overlay's quiet moment (128) and About's check and restart (129, 130), reach the user's host as named events in the user's session, `Local\thermi-sensors-<user SID>-<code>`. The stop event, `-stop`, is a fourth. Each is created by the host, with an access list for the user alone. A host that finds any of them already there (`ERROR_ALREADY_EXISTS`) does not run and logs why, so a process that made the name first never holds the host's stop. Unlike a pipe, an event carries no data to parse.

## Rejected

- **The user's host on the service's pipe name**, as first asked. One user's host would hold the name against the service and every other session, and a pipe the overlay trusts by its owner cannot be both SYSTEM's and a user's.
- **Converting the per-user folder in place.** A SYSTEM service must never run from a folder a user writes (ADR 0003), so the machine install copies the version under Program Files.
- **`msiexec` behind `runas`.** UAC would then name Microsoft's Windows Installer, not the package, so a process of the user's could swap the MSI after the check unseen.
- **One dual-purpose MSI** (`Scope="perUserOrMachine"`). Windows Installer keeps the two contexts apart, so an upgrade across them still needs a second install, and the choice moves into a property a person must know.
- **`thermi.exe` behind `runas` doing the install.** That is the hole ADR 0010 closes.
- **Reading the sensors in the overlay's process.** LibreHardwareMonitor would sit in the overlay's 48 MB budget and take it down when a device's read hangs (`docs/adr/0015-a-stalled-device-holds-back-none.md`).
- **A control pipe for the user's host.** It would be a second server that parses bytes, for four signals that carry no data.

## Consequences

- thermi installs and shows the GPUs, the CPU's load and memory without any prompt. The CPU's temperature, power and clocks, the fans and SMART come after one prompt, which an administrator answers once per PC.
- Two scopes double the installs to test. Each acceptance step that installs states its scope. The release builds, signs and lists four MSIs, and the manifest grows their entries.
- A per-user install belongs to one user. Other users on the PC install their own or ask for the machine one. Two users' hosts read the sensors twice; the service, once installed, serves both.
- The user's host is held to the service's budget, 80 MB and 3 % of a core.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md` says the sensor host runs as a LocalSystem service and that installing needs admin rights once. Instead, it runs as that service in a machine install and as the user's own process in a per-user one, and only the machine install needs admin rights.
- `docs/adr/0010-driver-and-service-from-the-window.md` says the driver's fix starts `thermi-elevated` through `sc.exe`. Instead, in a per-user install the fix runs the machine MSI, whose action starts the same service. In a machine install it is unchanged. The network rule now also allows the user's host to download the machine MSI.
- `docs/adr/0008-updates.md` says the service alone updates thermi and its control codes go to the service. Instead, the user's host updates a per-user install by the same rules, and its codes reach it as named events.
