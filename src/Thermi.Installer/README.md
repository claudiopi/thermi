# Thermi.Installer

thermi's MSIs, per machine and per user for each version and processor, built with WiX 5 from a version already published:
`pwsh scripts/updates.ps1 pack --version <v> --out <zip> --msi <msi>` builds the zip and both MSIs.

The MSI owns only a bootstrap, the version it ships, in `C:\Program Files\thermi\setup`, the Start menu entry and the
Active Setup entry that turns on the start at sign-in once in each profile. As SYSTEM it runs that version's
`thermi-sensors.exe --adopt`, which lays the version down as an update keeps it, and on uninstall `--remove`, which
takes it off (`docs/adr/0008-updates.md`).

Built with `ThermiScope=perUser` it is the per-user MSI, `thermi-<v>-<cpu>-user.msi`, with an UpgradeCode of its own
(`docs/adr/0016-admin-on-demand.md`): it installs without a prompt into `%LOCALAPPDATA%\Programs\thermi`, runs
`--adopt --user` and `--remove --user` as the user, and has no service, driver or Active Setup; the first install's adopt
turns on the user's start at sign-in. The machine MSI's secure `ADDDRIVER=1` installs the driver on a first install,
through the `thermi-elevated` service, ignoring what it answers.

Every program both run goes through WiX's util extension, `WixToolset.Util.wixext`, from its DLL native to the package's
processor: `WixQuietExec`, which shows no console window, and for the overlay after an install by hand
`WixUnelevatedShellExec`, which has Explorer start it as the person, unelevated even from an elevated console.
