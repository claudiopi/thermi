# Thermi.Installer

thermi's MSI, one per version and processor, built with WiX 5 from a version already published:
`pwsh scripts/updates.ps1 pack --version <v> --out <zip> --msi <msi>` builds both.

The MSI owns only a bootstrap, the version it ships, in `C:\Program Files\thermi\setup`, the Start menu entry and the
Active Setup entry that turns on the start at sign-in once in each profile. As SYSTEM it runs that version's
`thermi-sensors.exe --adopt`, which lays the version down as an update keeps it, and on uninstall `--remove`, which
takes it off (`docs/adr/0008-updates.md`).
