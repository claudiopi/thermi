# Releases and updates

How a version of thermi reaches PCs, the levers on it, and the checks a person runs before each release. Why it works this way is in `docs/adr/0008-updates.md`, and on Linux in `docs/adr/0019-linux.md`; what it must do is in `docs/specs/004-updates/spec.md` and `docs/specs/006-linux/spec.md`.

## The channels

The update server is static files: each channel's signed envelope, `stable.json` and `beta.json`, with a 60-second cache, and the zips under `zips/<version>/`, which never change once written. On Windows, each installed service checks its channel a minute after it starts and every 4 hours, takes a higher version its rollout reaches, unpacks it beside the running one, and switches at a quiet moment. Nothing about the PC is sent.

## The levers

`scripts/updates.ps1` runs them on a channels folder laid out as the server is; each writes the channel's next signed manifest. The private key comes from `--key` or `THERMI_UPDATES_KEY`.

| Lever | What PCs do |
| --- | --- |
| `publish --channel beta --version <v> --zip x64=<zip> arm64=<zip> [--msi x64=<msi> arm64=<msi>] --base-url <url> [--rollout 10] [--urgent]` | Take the version as their rollout number allows. Urgent skips waiting for idle time. The machine MSIs are what a per-user install runs to install for everyone (`docs/adr/0016-admin-on-demand.md`). |
| `promote [--from beta --to stable] [--rollout 100]` | Stable takes beta's version. |
| `rollback --channel stable` | The version is withdrawn: PCs on it go back to the version they kept, and the one before is offered. |
| `disable --channel stable` | PCs stop taking anything until the next publish. |
| `refresh --channel stable` | The same manifest, signed again; a manifest expires 45 days after it is signed. |
| `revoke --channel stable --release-key <release.pub.pem> --backup-key <backup.key.pem>` | The release key is revoked: PCs trust the backup key alone until a build with another release key runs. Signed with the backup key, or refused. |
| `status` | What each channel offers. |

A version that fails on a Windows PC is never taken there again: its service served nothing within 120 polls, or its second run ended without stopping within 10 minutes.

### On Linux

On Linux the system's own updater updates thermi, from thermi's apt and dnf repositories, static files under the server's `linux/` (`docs/adr/0019-linux.md`). The `.deb` and the `.rpm` add the stable one with the key it is signed with, and a person who wants beta changes `Suites:` in `/etc/apt/sources.list.d/thermi.sources` or the end of `baseurl=` in `/etc/yum.repos.d/thermi.repo`. `scripts/linux-packages.ps1` packs a version. Given `--repository <folder>`, the levers of `scripts/updates.ps1` act on the repositories instead, through `scripts/repository.ps1`: each writes the channel's indexes with apt-ftparchive and createrepo_c, from a Debian container where the PC has neither, and signs them with the OpenPGP key in `--key` or `THERMI_REPOSITORY_KEY`. The release workflow pulls them between `scripts/channel-sync.ps1 down -Linux` and `up -Linux`:

| Lever | What PCs do |
| --- | --- |
| `publish <v> --channel beta --packages <folder>` | Take the version with their next update. A version not above every one the channel published is refused. |
| `promote [--from beta --to stable]` | Stable takes beta's version, every PC at once: the repositories have no rollout. |
| `rollback --channel stable` | Take the version before, packed again from its tarball as `<highest>.really<version>`, which sorts above every version published, so the system's updater installs it. The AUR keeps the release it has: there, a rollback is a release of the version before by hand. |
| `disable --channel stable` | Find nothing to take until the next publish. |
| `sign --channel stable` | Nothing new: the same offer signed again, as apt refuses a `Release` 45 days after it is signed. The workflow's `refresh` and its monthly signing pull it. |
| `restore --before-last --channel stable` | Take the offer before the last again, indexed and signed anew, as apt takes no `Release` older than the one it holds. Only by hand, on a Linux PC, between `scripts/channel-sync.ps1 down -Linux` and `up -Linux`: the workflow has no such lever. |
| `status` | What the channel offers. |

## Trying it on Linux

```sh
pwsh scripts/linux-packages.ps1                  # artifacts/linux, with a key of the build's own in key/
pwsh scripts/updates.ps1 publish 0.9.0 --channel stable --packages artifacts/linux --repository <repositories>
sudo apt install ./artifacts/linux/thermi-sensors_0.9.0_amd64.deb ./artifacts/linux/thermi_0.9.0_amd64.deb
pwsh scripts/updates.ps1 serve --folder <a folder holding <repositories> as linux>
```

Given a build's packages, the levers sign with its own key, which those packages trust. Removing the `.rpm` leaves its key in rpm's own database, as every repository's does; `rpm -e gpg-pubkey-<id>` takes it out.

## Trying it on this PC

```powershell
pwsh scripts/updates.ps1 keys --out <keys>
pwsh scripts/updates.ps1 serve --folder <channels>
pwsh scripts/install.ps1 -UpdatesFrom http://127.0.0.1:8765/v1/ -UpdatesKeys <keys>
pwsh scripts/updates.ps1 pack --version 0.9.1 --out <zip> --updates-from http://127.0.0.1:8765/v1/ --updates-keys <keys>
pwsh scripts/updates.ps1 publish --folder <channels> --key <keys>/release.key.pem --channel stable --version 0.9.1 --zip x64=<zip> --base-url http://127.0.0.1:8765/v1/ --urgent
```

The service writes each step to the Application event log under `thermi-sensors`.

`pack --msi <msi>` also builds the version's MSIs: `<msi>`, which installs what `scripts/install.ps1` does, for every user, and beside it the same name ending in `-user.msi`, which installs thermi for the user alone with no prompt and no driver (`docs/adr/0016-admin-on-demand.md`). `msiexec /i <msi> /qn` installs either silently, and Installed apps uninstalls it.

## What the owner sets up

A release needs accounts and keys only the owner can hold. Do each step once, in this order.

| Step | What | Where it ends up |
| --- | --- | --- |
| 1. The channel's address | A domain for the update server, such as `updates.<your domain>`. Every release build carries `https://<it>/v1/`, so it never changes once a release is out. | The repository's variable `THERMI_UPDATES_FROM`, in step 8. |
| 2. The update server | A Cloudflare R2 bucket, `thermi-updates`, public for reading under that domain, with a 60 s cache rule for `/v1/*.json`, and an API token that writes only that bucket. | The `release` environment's `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`. |
| 3. The manifest keys | `pwsh scripts/updates.ps1 keys --out <folder>` on your own PC. `release.key.pem` goes online, then off the PC. `backup.key.pem` never goes online: keep two offline copies; it is only for `revoke`. | The secret `THERMI_UPDATES_KEY`; the public keys' text as the variables `THERMI_UPDATES_RELEASE_PUB` and `THERMI_UPDATES_BACKUP_PUB`, in step 8. |
| 4. Authenticode | SignPath Foundation's free signing for open source: a project `thermi` linked to the repo, three artifact configurations (`binaries`: every `.exe` and `.dll` of a version's zip; `catalog`: `thermi.cat`; `msi`: both MSIs), and a signing policy `release` that signs only for this repo's `release` environment. | The secret `SIGNPATH_API_TOKEN` and the variable `SIGNPATH_ORGANIZATION_ID`; the certificate's SHA-256 thumbprint as the variable `THERMI_UPDATES_SIGNERS`, in step 8. |
| 5. The Linux repositories' key | On a Linux PC: `gpg --quick-generate-key 'thermi <you@example.org>' rsa3072 sign never`, with no passphrase, then `gpg --armor --export-secret-keys` and `gpg --armor --export`. Every `.deb` and `.rpm` installs the public key and its repository trusts nothing else, so it never changes once a release is out; keep two offline copies. | The secret `THERMI_REPOSITORY_KEY`; the public key's text as the variable `THERMI_REPOSITORY_PUB`, in step 8. |
| 6. The AUR | An AUR account with an SSH key of its own, and the package `thermi-bin` made by pushing a first `PKGBUILD`, from the `aur` folder `scripts/linux-packages.ps1` writes. The AUR's host key, ssh-ed25519 and its key, from the Arch wiki's page on the AUR, checked against `ssh-keyscan aur.archlinux.org`. | The secret `AUR_SSH_KEY`; the host key as the variable `AUR_HOST_KEY`, in step 8. |
| 7. The GitHub environment | `release`, with you as its required reviewer and only `main` allowed to deploy to it. | Holds the secrets above. Every publish, promotion and monthly signing waits for your approval. |
| 8. The repository's variables | All four at once: `THERMI_UPDATES_FROM`, `THERMI_UPDATES_RELEASE_PUB`, `THERMI_UPDATES_BACKUP_PUB`, `THERMI_UPDATES_SIGNERS`. Linux's, `THERMI_REPOSITORY_PUB` and `AUR_HOST_KEY`, together once steps 5 and 6 are both done. | From then on every merge runs the release workflow. |
| 9. Branch protection | A ruleset on `main`: changes only through a pull request, squash-merged, with the one required status check `CI gate`, and no one may bypass it. `CI gate` passes only when every job of `.github/workflows/ci.yml` passed: Core's tests and the build's checks, the Windows tests, the Windows tests that run alone, Core's tests on Linux, the Linux tests, the page, the page on Linux, the MSIs, the PR's title, and `.github/workflows/security.yml`'s: no secret in the history, no package with a known vulnerability, and none added by the PR. Branches need not be up to date before merging, or Renovate's auto-merges wait forever. | The repository's settings, under Rules. Jobs can change without touching it. |
| 10. Renovate | The free Mend Renovate app, installed on this repository alone, and Allow auto-merge on, with squash merging the only method allowed. | `renovate.json` drives it: it opens the dependency PRs and auto-merges build tools' and actions' once `CI gate` passes. |
| 11. The GNOME extension | An account on extensions.gnome.org. Upload `src/Thermi.Gnome` zipped, its files at the zip's root, under the uuid `thermi@claudiopi.github.io` that `metadata.json` names, and wait for GNOME's review, which takes days. Upload it again for each release that changes a file of `src/Thermi.Gnome` or the Shell versions, before the release goes out. | The site serves it to `thermi`, which asks GNOME to install it on first start. A release whose extension adds to the protocol still shows the overlays through the site's earlier one, without what is new, with a notice; only an extension without the overlay's own calls shows nothing, with a notice that says to update it. |

Why some of it is this way:

- Until step 8, a merge skips the release workflow with a notice. Without steps 5 and 6 the Windows release goes on, and the Linux jobs publish nothing, with a notice in the run's summary (`scripts/linux-ready.ps1`). Set none of step 5's and 6's four values until both steps are done: with some of them set, every run fails. With only some variables set, Windows' or Linux's, every run fails, rather than build a release that never updates or let a channel expire behind green runs. A release run again after its Linux job published but failed to push to the AUR only pushes to the AUR.
- Every lever opens the channels' signed histories with the public keys, so whoever can write the bucket cannot have a lever sign what they planted. On your PC, the levers take the keys' folder as `--trust <folder>`.
- The thumbprint is what `(Get-AuthenticodeSignature <signed file>).SignerCertificate.GetCertHashString('SHA256')` shows. It is a thumbprint, not a name, because SignPath Foundation's certificate carries the same name for every project it signs. Ask SignPath whether the certificate is thermi's own: a shared one means the catalog proves only that SignPath signed the files.
- When the certificate is renewed, list both thumbprints, separated by a semicolon, in a release made before the old one's last day. A PC that skips that release refuses every later one until a later MSI is installed on it.
- The MSI is built with WiX 5, which needs no account or fee.
- GNOME lets no program draw above its top bar, so the overlay on GNOME needs the extension, which GNOME installs only from extensions.gnome.org and after its own review. The site keeps each version it approved, so a person on an older GNOME gets the newest that names their Shell.

## The release workflow

`.github/workflows/release.yml` does the rest:

- Each merge to main goes to beta as `<version>-beta.<run>`.
- Run by hand with `release`, it puts `Directory.Build.props`' version on stable at the rollout given, with its MSIs on a GitHub release. With `promote`, `rollback`, `disable` or `refresh` it pulls that lever.
- On the first of each month it signs both channels again, before their manifests expire.
- Each job that signs waits for your approval: a beta asks twice, a release three times, a lever by hand and the monthly signing once. Each Linux job waits at the same moment as one of Windows', and one approval of the environment releases both.
- `scripts/channel-sync.ps1` moves the channels between the run and the bucket, the zips before the manifests, and writes a manifest only if the bucket still holds what the run fetched. Of two runs at once, the second fails and is run again.
- Right after a stable release, raise `Directory.Build.props`' version: a beta of the version released is older than it, and the channel refuses it.
- The Linux packages of the same version go to the same channel of the repositories, and a release's `PKGBUILD` to the AUR once its tarballs are served; each lever by hand acts on the repositories too, `refresh` as `sign`, and the monthly signing signs them again. `scripts/channel-sync.ps1 -Linux` moves them, the packages first and the indexes after the histories.
- `revoke` never runs there, since it needs the backup key: on your PC run `scripts/channel-sync.ps1 down`, then `revoke`, then `scripts/channel-sync.ps1 up`.

## Before each release

The specs are proven by tests alone. What only a person can see, on the development PC and its installed build, is checked here before a version goes to stable; none of it holds back a spec, and a spec's requirement names the check that covers what its tests cannot (`docs/specs/README.md`). What needs other hardware, such as Windows 10, Arm64 or a laptop, comes from [Hardware reports](https://github.com/claudiopi/thermi/issues/new?template=hardware-report.yml) instead.

Run them on the release's build, installed; its Release run lists them in its summary, from `scripts/release-checks.ps1`. A check that fails is a bug: it starts with a test that reproduces it, where one can (`docs/BUGS.md`).

### Install and start

| Check | Steps | Passes when |
| --- | --- | --- |
| **Install the release's MSI** | Uninstall thermi. Run the release's x64 machine MSI, then uninstall it from Installed apps. | One UAC prompt installs thermi, both services, the Start menu entry and the start at sign-in, with nothing downloaded. `Get-AuthenticodeSignature` says Valid for every `.exe` and `.dll` under `%ProgramFiles%\thermi`. The uninstall removes that folder whole and keeps `%APPDATA%\thermi`. |
| **Install over the previous release** | With the previous release installed and used, a saved style, Start with Windows off and a tray value shown, install the new MSI over it. Let an update and a rollback run. Run `scripts/install.ps1` over it. | Each time, the settings, the style, the log, Start with Windows and the shown tray value are as they were, and one overlay runs. |
| **Restart and sign-in** | Drag an overlay somewhere and snap another to the second monitor. Restart and sign in. Turn Start with Windows off, reinstall, sign out and in. | The overlay is on the taskbar with live values and no UAC prompt, each overlay is where it was, and the snap finds its monitor. With the start off, no overlay starts, and Ctrl+Z in the window never touched the switch. |
| **A standard user's install** | On a standard account with UAC at its default, install the per-user MSI, sign out and in, then double-click the CPU's temperature and consent as an administrator. Then run the machine MSI from an administrator's elevated console, a repair with `msiexec /fa`, and an install that fails after its adopt step, each logged with `/l*v`. | The per-user MSI asks nothing and shows the GPU and the CPU's load, with the temperature as "–". After the sign-out, the user's host ended with the Stopped mark in `update\state.json`, and no crash is counted. After the consent, the machine install, the service and the driver arrive, the temperature reads, and the per-user install is gone with the settings kept. The overlay never runs elevated, a repair shows no console and puts back the services and the launchers, and the failed install leaves the PC as before. |
| **A sensors-only install** | Uninstall thermi. Run `scripts/install.ps1 -SensorsOnly` for a test channel, as in "Trying it on this PC", sign out and in, and run `thermi-cli read`. Publish the next build to the channel. Then install the full thermi over it. | One administrator prompt installs the services, `thermi-cli` and the driver when it is missing, no overlay starts, and `thermi-cli read` gives a CPU temperature. The update installs the sensors alone at the new version. The full install adds the overlay and the window. |

### Updates

| Check | Steps | Passes when |
| --- | --- | --- |
| **An update from a test channel** | As in "Trying it on this PC", publish a newer build and leave the PC idle. Roll the channel back. Publish a build made to fail at start. Open About with a newer build published. Do the first publish again with a per-user install. | The PC switches with no prompt and the overlay never blinks. The rollback takes it back within a check. The failing build goes back by itself, and the event log says why. About says it checks, then that the version is ready with Restart to update, which switches and closes the window; reopened within a minute, it checks nothing. The per-user install switches too. A day later the service is still under 80 MB. |
| **The update server's cache** | `curl -I` a channel's manifest and a zip on the update server. | The manifest says `max-age=60` and the zip `immutable`. |

### Costs

| Check | Steps | Passes when |
| --- | --- | --- |
| **Costs at rest** | Run `scripts/measure.ps1`, then with `-Window`. Again with Fade near the pointer on, with an agent's MCP server running, and with a game presenting at 144 FPS in front. Then `logman query -ets` with no client connected. | Every budget of `AGENTS.md` holds each time: the overlay 48 MB and 0.5 % of one core, the idle window with its renderer 1 %, the service 80 MB and 3 %, on this PC's two drives and two adapters. No thermi-frames session is listed. |
| **The window's speed** | Open the window with `--trace` the first time after sign-in, and again. Drag a knob for a minute. Use it for an hour. Close it. | The first frame shows within 2 s the first time and 1 s after. Each paint lands within 300 ms of its input, at most 10 times a second, and the overlay stays under 2 % of one core. Its memory stays within 5 MB of its first minute. Once closed, no window or WebView2 process is left. |

### The overlay

| Check | Steps | Passes when |
| --- | --- | --- |
| **On top, hidden and shown** | Click the taskbar, open Start and search. Sleep and wake the PC, and turn a monitor off and on. Play a full-screen video and a borderless game. Press the four hotkeys, one of them held by another app. | The overlay stays on screen and on top, hides from full-screen apps, and follows each hotkey; the one another app holds is refused. |
| **Monitors and taskbars** | Move the second monitor to another port. On a 1080p monitor at 100 % and a 4K one at 150 %, set 400 % with 36 px text and a pill color, free, in each corner and on the taskbar. Drop the overlay along the taskbar, restart Explorer, change the resolution and the scale, and drop one half off the screen. | The snap still finds its monitor. The overlay paints sharp and whole, as high as the taskbar on it, and the preview and Copy as image match it. It follows the taskbar within a second at the same offset, and shows whole inside the work area. |
| **Gestures on the desktop** | With four overlays on two monitors and a crowded taskbar: click values with the window open; hold pills and items until they lift and drag them over their own pill, onto another overlay and onto the desktop; drag before the hold; press Esc mid-drag; merge and resize overlays; open and close apps; undo a gesture from the overlay's menu; use the Size menu and Ctrl+wheel, free, in a corner and on the taskbar. | The window follows. A lifted pill or item joins where it is let go, or becomes an overlay that snaps; a drag before the hold moves the whole overlay; Esc puts everything back and reaches no other app. No overlay on the taskbar moves as apps open and close, and none grows past its height. A spin undoes whole, and "Custom…" lands on the Size slider. |
| **The overlay reads well** | On light and dark wallpapers and taskbars, go through each theme, font, effect and a 0 % background, each level, the first critical and a stopped service. Look at thermi's icon and three item icons at 100 %, 150 % and 200 %, across a restart, an Explorer restart and level changes. | Every value reads well, the levels read without color, the glow comes once, and the tooltips say what to do. The icons read at a glance, keep their pins and come back. |
| **Menus, notices and the overlay's additions** | In English and Spanish, light, dark and high contrast, right-click a pill, the edge and an item's tray icon, and choose each entry by mouse and by its key. Open Task Manager from both menus. Point at the CPU and GPU values during `thermi cpu-load 30`. Copy the overlay as an image into Paint. Turn on Fade near the pointer. Remove the driver. Then see the first start's notice, hide the overlays from thermi's menu, turn click-through on, and let an item with Notify me go critical. | Every entry does what it says at once, and Undo takes it back. The menus are Windows' own and follow the theme. The tooltips name the models, the busiest process and any throttling. The picture is the overlay. The overlay fades and still takes a double-click. The CPU pill says Driver, and its double-click opens the window on the driver's bar. Each notice comes from thermi's icon and its buttons work, and clicks pass through until click-through is off. |
| **Recover from failures** | End the service from Task Manager, then stop and start it. Open the window without the overlay. Install the driver refusing Windows' prompt, then accepting it. Edit the settings file by hand, break it with a typo, restore it. Crash the overlay after its first minute, then hang it until Windows offers to close it. End it from Task Manager, and with "Exit thermi". | The values are back within 10 s. The window and the overlay follow each change and recover on their own, and a broken file shows as broken and keeps its text. Windows offers to restart a crashed or hung overlay and one overlay comes back, with no settings window; nothing starts it again after Task Manager or "Exit thermi". |

### The settings window

| Check | Steps | Passes when |
| --- | --- | --- |
| **Open the window every way** | With the window closed, and then behind others, open it from the overlay's menu, thermi's menu, a double-click on a value and on the edge, `thermi settings`, and Start. | One window opens on the right item or overlay, or comes to the front focused. |
| **Narrator, themes and the window's frame** | With Narrator, and in light, dark, another accent, high contrast and with animation effects off, move through the window. Resize, maximize, snap and activate it over the overlays, and maximize and restore it from its caption button and its system menu. Type in the catalog's search and a Sensor dropdown's filter. Set Theme to Light, Dark and Windows, and open the window again in Light. | Everything is read with its name, role and value, and looks right. Nothing animates. The focus stays where it was, also once Esc closes the system menu. The edited overlay stays above the window unless it would cover the caption buttons. The search's marks read in both themes. Each theme applies at once to the page, the title row, the caption buttons and Mica, and the window opened in Light is light from its first frame. |
| **Spanish and long texts** | With Windows in Spanish, go through both menus, every page and the catalog. Then again with `thermi settings --pseudo`. | Every text thermi writes is Spanish, and long texts fit. |

### Sensors

| Check | Steps | Passes when |
| --- | --- | --- |
| **Values against another monitor** | Show another hardware monitor, such as HWiNFO, beside the overlay for a minute. | The values agree. |
| **CPU load against Task Manager** | Keep Task Manager's CPU utilization beside the overlay at rest and during `thermi cpu-load 30`. | CPU load stays within 3 points of it. |
| **Network and disks** | With WSL or Hyper-V and a VPN on, download a large file, and copy one between drives. | The NET pill follows the physical adapter, the disk items follow the busy drive, and the catalog lists both groups. |
| **The fan finder's load** | With Task Manager open, run the fan finder and cancel it. | Every core is loaded at idle priority, and the load ends within a second of Cancel. |
| **FPS against Zwift and PresentMon** | Ride Zwift windowed, borderless and in full screen, and run free Direct3D 9, 11 and 12 and Vulkan demos beside PresentMon, a minute each. Then give the focus to a terminal maximized on the other monitor that streams text. | The window's FPS is within 2 % of Zwift's own counter and of PresentMon's, and keeps showing Zwift while the terminal has the focus. |

### Agents

| Check | Steps | Passes when |
| --- | --- | --- |
| **thermi-cli in Claude Code and Claude Desktop** | Add `thermi-cli` with the README's one command. Ask a new session how hot the CPU is, then to set its warning at 70 °C. Then the same in Claude Desktop. | The answer matches the overlay, and the overlay shows the new warning. |

### Linux

Run them with the release's packages on the physical Ubuntu 26.04 PC with GNOME, which has real sensors, and on the test machines it runs on libvirt, each reset first with `virsh -c qemu:///system snapshot-revert <machine> clean`, a snapshot with nothing of thermi's:

| Machine | What it tries |
| --- | --- |
| `thermi-fedora` | Fedora 45 with GNOME 51, the newest Shell, Wayland: the `.rpm`, and a second account, `thermi2`, for the prompt declined. |
| `thermi-ubuntu2404` | Ubuntu 24.04 with GNOME 46, the oldest Shell the extension takes: the `.deb`. |
| `thermi-debian` | Debian 13 with GNOME 48: the `.deb`. |
| `thermi-kubuntu` | Kubuntu 26.04 with KDE Plasma 6.6, Wayland: the `.deb` and the layer surface. |
| `thermi-arch` | Arch with Hyprland: `thermi-bin` from the AUR and the layer surface. |
| `thermi-server` | Ubuntu Server 24.04 with no screen: `thermi-sensors` alone and `thermi-cli`. |

Each desktop has two monitors. A virtual machine reads no sensors, so there its values show as missing. `virsh -c qemu:///system screenshot <machine> <png> --screen 0` captures a screen.

| Check | Steps | Passes when |
| --- | --- | --- |
| **Install on Linux** | On each, install `thermi-sensors` alone with the distribution's tool and run `thermi-cli read`; on `thermi-server`, nothing more. Install `thermi` while signed in; on GNOME, accept GNOME's prompt for the extension, and on another account decline it and sign out and in. Remove both. | On the physical PC, `thermi-cli read` gives a CPU temperature and power. The overlay shows at once, on GNOME after the prompt, and after the sign-in where it was declined, with a notice until then. The removal stops the service and the overlay and leaves `~/.config/thermi`. |
| **Restart and sign-in on Linux** | On each, drag an overlay into a corner, on GNOME another onto the top bar, and on the physical PC snap one to the second monitor. Restart and sign in. Kill `thermi` with `systemctl --user kill --signal=KILL thermi`, as a crash ends it, then choose Exit thermi. Turn the start at sign-in off with `thermi-cli start-with-windows off`, sign out and in. | Each overlay is where it was, above every window, and covered by a full-screen video. Killed, one overlay comes back; after Exit, none. With the start off, none starts. |
| **Costs at rest on Linux** | On the physical PC, with the overlay showing for 10 minutes, read the overlay's and the service's memory and CPU, and GNOME Shell's CPU with thermi running and without. Open the window on an overlay, whose preview it paints each second, and leave it idle for a minute. | The overlay holds 48 MB and 0.5 % of one core, the service 80 MB and 3 %, the idle window with WebKit's processes 1 %, and GNOME Shell takes at most 0.5 % of one core more with thermi. |
| **An update on Linux** | On `thermi-fedora` and `thermi-kubuntu`, installed from the previous release, publish the release to a test repository with the levers, run the system's updater and leave the session idle. Roll the channel back and update again. | The system's updater offers thermi beside the system's own updates. After it, the service runs the new version, each overlay hands over at the quiet moment without blinking, and About says the version, the channel and that updates come from the system. The rollback brings the version before. |
| **The window's speed on Linux** | On the physical PC, after sign-in, stop the overlay with `systemctl --user stop thermi` and run `thermi --trace`. Open the window with `thermi settings --trace` the first time, and again. Move a slider once, then drag a knob for a minute. Close it. | The first frame shows within 2 s the first time and 1 s after. Each paint lands within 300 ms of its input, the first edit's too, from each `input` line of `~/.local/state/thermi/thermi-window.log` to the `edit-applied` line `thermi --trace` prints after it, on the same clock, at most 10 times a second. Once closed, no process of the window is left. |
| **Open the window every way on Linux** | On each desktop, with the window closed and then behind others, open it from the overlay's menu, thermi's icon, a double-click on a value, `thermi settings` and the app launcher. Close it. | One window opens on the right item or overlay, or comes to the front focused. Once closed, no process of it is left. |
| **Orca, themes and the window's frame on Linux** | On GNOME and KDE Plasma, with Orca, and in light, dark, another accent and high contrast, move through the window; resize, maximize and restore it. | Everything is read with its name, role and value, the window follows each scheme, accent and contrast, and its frame is the desktop's. |

## If the release key is stolen

Revoke it on both channels, as above. From then on every manifest takes only the backup key's signature, so the workflow refuses to sign until you clear it; meanwhile run each lever on your PC with the backup key, the monthly `refresh` too, or the manifests expire.

1. Run `keys --out <a new, empty folder>`: it never writes over a key. Keep only its `release` pair; the backup pair stays the one every PC trusts.
2. Put the new private key in `THERMI_UPDATES_KEY` and its public one in `THERMI_UPDATES_RELEASE_PUB`, and merge. The run's publish refuses, since the channel is revoked.
3. Download the run's `zip-*` artifacts, and its `msi-*` ones for stable. On your PC run `scripts/channel-sync.ps1 down -Version <version>`, then `publish` with `--key <backup.key.pem>`, then `scripts/channel-sync.ps1 up`. For stable, `gh release create v<version> <the MSIs>`.
4. After 45 days, the manifests' lifetime, run `unrevoke` on each channel on your PC with the backup key, between `down` and `up`.

A PC offline through all of it still trusts the stolen key, and needs the MSI of a later version.
