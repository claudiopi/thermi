# Thermi.Installer

thermi's MSIs, built with WiX 5 from a version already published. `pwsh scripts/updates.ps1 pack --version <v> --out <zip> --msi <msi>` builds the zip and both MSIs.

The MSI owns almost nothing. It carries a bootstrap, the version it ships, and runs that version's `thermi-sensors.exe --adopt`, which lays the version down exactly as an update keeps it; uninstalling runs `--remove` (`docs/adr/0008-updates.md`). So an MSI install and an update can never disagree about the layout.

| | Per machine | Per user (`ThermiScope=perUser`) |
| --- | --- | --- |
| File | `thermi-<v>-<cpu>.msi` | `thermi-<v>-<cpu>-user.msi`, an UpgradeCode of its own |
| Folder | `C:\Program Files\thermi` | `%LOCALAPPDATA%\Programs\thermi` |
| Prompt | one UAC prompt | none |
| Sensors | the `thermi-sensors` service, as SYSTEM | the user's own host, without admin rights (`docs/adr/0016-admin-on-demand.md`) |
| Start at sign-in | Active Setup, once in each profile | the first install's `--adopt --user` |
| Options | `ADDDRIVER=1` installs the driver; `SENSORSONLY=1` the sensors and `thermi-cli` alone | none |

Both put `thermi-cli` on `PATH`. Every program they run goes through WiX's util extension, so no console window shows, and the overlay after an install by hand starts unelevated through Explorer.

`Package.wxs` is the package; `Thermi.Installer.wixproj` builds it.
