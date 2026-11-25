# 0016. thermi installs and runs without administrator rights; one prompt adds what needs them

**Status**: Proposed

## Context

thermi needs administrator rights today to install (`docs/adr/0003-sensor-service.md`: a LocalSystem service under Program Files) and once more for the driver (`docs/adr/0010-driver-and-service-from-the-window.md`). Many people, on a work PC or as a standard user, cannot or will not say yes to that before they have seen anything, and competitors sell "no admin needed". Most of what thermi shows needs no rights at all. A dump without them on this PC (`tests/Thermi.Tests/Recordings/ryzen-9950x-rtx-5060-ti-no-admin.json`) reads the CPU's load, both GPUs' temperatures, power, clocks and loads, and memory. Missing are the CPU's temperature, power and clocks, the board's fans and the drives' SMART, which need the Administrators group and, for the CPU, the driver. The 002 overlay already shows those as "–" with "Double-click to fix".

## Decision

- **Two scopes of one version.** Each release ships two MSIs built from one WiX source:
  - `thermi-<v>-<cpu>.msi`, per machine, as now.
  - `thermi-<v>-<cpu>-user.msi`, per user, installed without a prompt to `%LOCALAPPDATA%\Programs\thermi`, with the same layout: `<v>\`, launchers at the top, `update\`.

  winget offers both, by its `Scope`. The per-user install registers no service and no driver, and starts the overlay at sign-in through the user's Run key, as the machine install's Active Setup does.
- **The sensors as the user.** Without the `thermi-sensors` service, the overlay starts `thermi-sensors.exe --user` from the top launcher: a hidden process of the user's, the same host and LibreHardwareMonitor, which reads what the user's rights allow. It runs only while a client is connected and ends when the last one leaves, as the service reads only then. It serves the same protocol on a pipe of the user's own, `thermi-sensors-<user SID>`. A global name would let one user's host block the service and every other user's overlay. The overlay trusts that pipe only when its owner is the user's own SID, so another user who takes the name first is never read. It uses the service's pipe whenever the service is installed, and its own only when the service is not. A host that dies is started again at the overlay's next try, every 2 s, as Windows' recovery restarts the service.
- **One prompt adds the rest.** In a per-user install, "Double-click to fix" on a "–" and the window's fix offer **Install for everyone**. thermi runs the machine MSI of the version it runs through Windows' own `msiexec.exe`, with the `runas` verb and `ADDDRIVER=1`: one UAC prompt that names thermi's signed publisher. Its custom actions then lay the version down as today, register both services, and install the driver through `thermi-elevated install-driver` (ADR 0010's path), as SYSTEM with the machine's environment. A driver that fails leaves thermi installed and logs why, as the window's fix does.
  - `msiexec` is native, so no .NET hook in the user's environment loads into it, and its deferred actions run under the Windows Installer service, never with the user's environment. That is the same guarantee ADR 0010 gets from `sc.exe` and a service.
  - The MSI comes from the update channel: each version's entry in the signed manifest lists its machine MSI's URL, size and SHA-256. The user's host downloads it into its own folder and checks size and hash before the prompt.
  - Once the service serves, the overlay switches to its pipe. It removes the per-user install, an `msiexec /x` of the user's own product that needs no rights, keeping the settings in `%APPDATA%\thermi`, which both installs share.
- **Updates in both scopes.** The service's updater is unchanged. In a per-user install, the same `Updater` runs in the user's host, with `InstallFolder` rooted in the user's folder:
  - It checks, downloads and verifies exactly as the service does: manifest signature, sequence, expiry, hashes, and in a release build the signed catalog.
  - It switches by placing the new version's launchers and starting its host. The watchdog, `thermi-sensors --switch --user`, waits for the new version's pipe and otherwise puts the old launchers back, with the same 120 polls.
  - The overlay hands over as it does now.
  - The trusted-folder check becomes "a folder only this user and administrators write". Anyone who can write it already runs as the user, so a per-user install trusts nothing a machine install would not.
  - The per-user updater asks nothing of the service and the service nothing of it. About's line and the control codes of ADR 0008 read and reach the scope that is installed. In a per-user install the codes become calls on the user's host through a second pipe of its own, `thermi-control-<user SID>`, owned by the user, carrying the same three codes as single bytes.
- **Settings and data** stay where they are for both scopes: `%APPDATA%\thermi` and `%LOCALAPPDATA%\thermi`.

## Rejected

- **The user's host on the service's pipe name**, as first asked: one user's host would hold the name against the service and every other session, and a pipe the overlay trusts by its owner cannot be both SYSTEM's and a user's.
- **Converting the per-user folder in place**: a SYSTEM service must never run from a folder a user writes (ADR 0003), so the machine install copies the version under Program Files.
- **One dual-purpose MSI** (`Scope="perUserOrMachine"`): Windows Installer keeps the two contexts apart, so an upgrade across them still needs a second install, and the choice moves into a dialog or a property a person must know.
- **`thermi.exe` behind `runas` doing the install**: the hole ADR 0010 closes; .NET would load what the user's environment names before thermi's code runs.
- **Reading the sensors in the overlay's process**: LibreHardwareMonitor would sit in the overlay's 48 MB budget and take it down when a device's read hangs (`docs/adr/0015-a-stalled-device-holds-back-none.md`).

## Consequences

- thermi installs and shows GPU, CPU load and memory without any prompt. The CPU's temperature, power and clocks, the fans and SMART come after one prompt, which an administrator answers once per PC.
- Two scopes double the installs to test: each acceptance step that installs states its scope. The release builds, signs and lists both MSIs, and the manifest grows their MSI entries.
- A per-user install is the user's own: other users on the PC install their own or ask for the machine one. Two users each running a host read the sensors twice; the service, once installed, serves both.
- A per-user host and the service can briefly both run while one prompt installs the service. The overlay always prefers the service's pipe, and the per-user host ends as its last client leaves.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md` says the sensor host runs as a LocalSystem service and installing needs admin rights once. Instead, it runs as that service in a machine install and as the user's own process in a per-user one, and only the machine install needs admin rights.
- `docs/adr/0010-driver-and-service-from-the-window.md` says the driver's fix starts `thermi-elevated` through `sc.exe`. Instead, in a per-user install the fix runs the machine MSI through `msiexec`, whose actions install the driver by the same elevated mode. In a machine install it is unchanged.
- `docs/adr/0008-updates.md` says the service alone updates thermi and its control codes go to the service. Instead, the user's host updates a per-user install by the same rules, and its codes go to it through its control pipe.
