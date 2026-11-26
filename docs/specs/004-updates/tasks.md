# 004-updates: Tasks

**Status**: built in v1; how it was built is in git's history.

## Open

- **Install the first signed release on clean PCs**: run the published release's machine MSI on a clean Windows 11 PC, x64 and then Arm64; it passes when one UAC prompt installs thermi, both services, the Start menu entry and the start at sign-in with nothing downloaded, every executable and library is signed, and uninstalling removes the folder and keeps the settings (A1).
- **Install a release over the one before it**: with the previous release installed and used, install the new MSI, then let an update and a rollback run; it passes when the settings, a saved style, the log, "Start with Windows" off and a shown tray value are as they were each time (A2).
- **The user's host ends cleanly at sign-out and restart**: with a per-user install whose host serves, sign out, and then restart Windows; it passes when `update\state.json` has the Stopped mark each time and the next start counts no crash (A34).
- **The MSIs from an elevated console, a standard account and a repair**: on a PC or VM with UAC at its default and a standard account, run each `msiexec` case of A42 with `/l*v`; it passes when the overlay always starts unelevated or refuses to run elevated, a repair shows no console and puts back the services and the launchers, and a failed install is undone (A42).
