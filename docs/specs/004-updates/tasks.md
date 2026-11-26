# 004-updates: Tasks

**Status**: built in v1; how it was built is in git's history.

## Open

Checks on hardware other than the development PC: [Hardware reports](https://github.com/claudiopi/thermi/issues/HARDWARE_REPORTS) (A1 on clean x64 and Arm64 PCs, A34 with a per-user install).

By hand on the development PC, before publishing:

- **Install a release over the one before it**: with the previous release installed and used, install the new MSI, then let an update and a rollback run; it passes when the settings, a saved style, the log, "Start with Windows" off and a shown tray value are as they were each time (A2).
- **The MSIs from an elevated console, a standard account and a repair**: on a VM with UAC at its default and a standard account, run each `msiexec` case of A42 with `/l*v`; it passes when the overlay always starts unelevated or refuses to run elevated, a repair shows no console and puts back the services and the launchers, and a failed install is undone (A42).
