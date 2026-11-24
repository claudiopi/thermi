# 0008. A signed MSI, updated by the service from a signed manifest

**Status**: Proposed

## Context

thermi is installed by building the repo (`scripts/install.ps1`). A public release needs an installer that Windows and people trust, and updates that reach users without a UAC prompt each time. The only process that can install without a prompt is the `thermi-sensors` service, which runs as LocalSystem (`docs/adr/0003-sensor-service.md`). Making it the updater turns it from a process that only publishes readings into one that downloads and installs code as SYSTEM: the most dangerous thing thermi will ever do. Anyone who can steer what it installs owns the PC.

## Decision

**Installer.** A WiX MSI, per-machine, one per processor, self-contained, with a major upgrade on every release. Windows Installer gives what an update needs for free: a transaction that rolls back whole if the new service does not start, files in use handled through Restart Manager, a clean uninstall, and the format winget, Intune and admins expect. WiX builds from MSBuild with a NuGet SDK, like the rest of the repo. Since v6 it asks revenue-earning users for a maintenance fee; thermi earns none, and the build accepts its EULA explicitly.

**Signing.** Every file of thermi's and the MSI are Authenticode-signed in the release workflow, by a cloud signing service whose keys never leave its HSM, reached through GitHub's OIDC from an environment that waits for the owner's approval. No signing secret is in the repo, in GitHub's secrets or on a PC. The service is SignPath's free plan for open source: Azure Artifact Signing validates individuals only in the USA and Canada, and Certum's open-source certificate lives on a token on one PC. A signed build also enables `uiAccess` (`docs/adr/0004-above-the-taskbar.md`).

**Manifest.** `releases.json`, at a fixed address, lists the recent releases with each MSI's URL, size and SHA-256. A detached ECDSA P-256 signature covers its exact bytes, made by a non-exportable key in an HSM-backed key vault, from the same approved environment. The service has two public keys built in: that one, and an offline backup kept by the owner, which can sign a release that replaces the first. The .NET class library verifies it; the service gains no package.

**The updater** is code in the service, apart from the sensors. It downloads with BITS, verifies, keeps the installed version's MSI for rollback, and runs `msiexec` as SYSTEM at a quiet moment. Users steer it only through user-defined service control codes, plain integers Windows carries. It still never reads its pipe, nor any file a user can write. The pipe gains a hello line with a protocol number; new line kinds are ignored by old readers, and a breaking change takes a new pipe name, served beside the old one for a release.

**Threat model.**

| Threat | What stops it |
| --- | --- |
| Tampered manifest or MSI, on GitHub, the CDN or the wire | The manifest's signature, then the size and SHA-256 it signs, then Authenticode pinned to thermi's validated identity, not a thumbprint, since certificates last days. All three, before anything changes. |
| Downgrade to an older, vulnerable release, or replay of an old manifest | Never a version at or below the installed one or the highest ever installed; never a manifest published before the last accepted. Rollback reaches only the exact previous version, kept locally, and a rolled-back version is retried, so an attacker who crashes the new service can delay it, not block it. |
| DNS spoofing, a hostile proxy, TLS interception | TLS keeps the check private, but trust never rests on it: a man in the middle can only withhold an update or serve bytes that fail the signatures. Sizes are capped before anything is read. Withholding is visible: 14 days without a check shows in the window. |
| A non-admin user steering the update | Downloads and state live in `%ProgramData%\thermi`, which the MSI creates with SYSTEM and administrators as its only writers; `%ProgramData%` lets users create folders, so its owner, access and the absence of junctions are checked before each use. Never `%TEMP%`, which users can write. The verified file is held open, shared only for reading, until `msiexec` ends. `msiexec` runs by full path. Users get only query and user-defined control on the service, never its configuration; a code is an integer, rate-limited, and the worst a user can do is check now or apply a genuine newer release early. The channel and the automatic switch are for the whole PC, so only administrators change them, in a file only they can write. |
| The overlay started again into a session | The fixed path under Program Files, with the session user's own unelevated token from `WTSQueryUserToken`, never the linked elevated one. |
| Theft of the signing keys | Keys never leave an HSM; signing needs the owner's approval of a run from a version tag; provenance attestations make every release traceable to its commit. A stolen manifest key alone cannot pass Authenticode, nor the reverse. A lost key is replaced by the backup key. |
| Code or UI from the network at run time | None. The update is the only download; release notes are plain text; a WebView2 window would serve only files embedded in `thermi.exe`. A repo rule bans network APIs outside the updater. |

**Privacy.** A check is one anonymous HTTPS GET of the manifest, no query, no cookie, no identifier, `thermi` as the whole User-Agent; GitHub sees the PC's IP address, as any download does. Nothing else ever leaves the PC.

**Offline and proxies.** BITS as SYSTEM uses the machine's WinHTTP proxy and auto-detection, resumes across reboots, and holds background downloads on metered networks. A check that fails is retried at the next; nothing tells the user before 14 days. A proxy that only a user's credentials pass is not supported: those users install the MSI by hand.

## Rejected

- **Velopack**: modern deltas and a simple API, but it installs per user into `%LOCALAPPDATA%`, and has no service support. A LocalSystem binary in a folder the user can write is what ADR 0003 forbids.
- **Inno Setup**: one friendly EXE, but no transaction: an upgrade that fails halfway leaves a broken install for the service to recover from on its own.
- **MSIX**: clean updates and rollback, but a packaged service needs a restricted capability, App Installer updates per user, and LibreHardwareMonitor's driver access and `uiAccess` inside the package are unproven.
- **A second service only for updates**: whatever runs `msiexec` as SYSTEM can do anything, so splitting it from the sensors buys little isolation for a second service to install, sign and keep in step.
- **Framework-dependent with a bootstrapper**: smaller updates and a runtime Windows can patch, but a bootstrapper EXE wrapping the MSI, and a SYSTEM service depending on a runtime it does not control.
- **The overlay downloading as the user**, to pass user proxies: the service would then read a path a user chose; signatures would make it safe, but it adds an input to the SYSTEM process for few PCs.
- **Sigstore verification in the service**: no mature .NET verifier; provenance is published for people to check by hand instead.
- **Pinning GitHub's TLS certificates**: they rotate, and trust already comes from the signatures.

## Consequences

- One install path for developers, winget and updates: `scripts/install.ps1` builds and installs the same MSI, unsigned and with updates compiled off.
- Updates are about 35 MB, since the runtime ships inside. A .NET security release is a thermi release: Renovate's SDK update must ship within a week of Microsoft's.
- The release depends on two cloud services, the signing service and the key vault, both billed monthly. If either is gone, users can still install a release by hand.
- Watch Restart Manager closing the overlay of older versions, and the overlay coming back in other users' sessions, on real PCs before the first public release.
- A release that changes the settings format must keep the copy and rollback rule of spec 004 (R18).

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md` says the host accepts no commands beyond sending readings. Instead, it also accepts the update's control codes, integers from the service control manager; its pipe is still write-only.
- `docs/adr/0003-sensor-service.md` rejects a service the overlay sends commands to, and says installing or updating needs admin rights once. Instead, interactive users may send the update's check and apply codes, and updates after the first install need no admin rights; the channel and the automatic switch still need them.
- `docs/adr/0006-proven-libraries-for-logging-and-win32.md` generates Win32 declarations in the overlay and the Windows tests. Instead, the service uses CsWin32 too, at build time only, for BITS, `WinVerifyTrust` and the session calls.
- `scripts/install.ps1`'s framework-dependent publish with a shared runtime, which `docs/adr/0006-proven-libraries-for-logging-and-win32.md` measured. Instead, thermi is published self-contained.
