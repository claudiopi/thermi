# Releases and updates

How a version of thermi reaches PCs, and the levers on it. Why it works this way is in `docs/adr/0008-updates.md`; what it must do is in `docs/specs/004-updates/spec.md`.

## The channels

The update server is static files: each channel's signed envelope, `stable.json` and `beta.json`, with a 60-second cache, and the zips under `zips/<version>/`, which never change once written. Each installed service checks its channel a minute after it starts and every 4 hours, takes a higher version its rollout reaches, unpacks it beside the running one, and switches at a quiet moment. Nothing about the PC is sent.

## The levers

`scripts/updates.ps1` runs them on a channels folder laid out as the server is; each writes the channel's next signed manifest. The private key comes from `--key` or `THERMI_UPDATES_KEY`.

| Lever | What PCs do |
| --- | --- |
| `publish --channel beta --version <v> --zip x64=<zip> arm64=<zip> --base-url <url> [--rollout 10] [--urgent]` | Take the version as their rollout number allows. Urgent skips waiting for idle time. |
| `promote [--from beta --to stable] [--rollout 100]` | Stable takes beta's version. |
| `rollback --channel stable` | The version is withdrawn: PCs on it go back to the version they kept, and the one before is offered. |
| `disable --channel stable` | PCs stop taking anything until the next publish. |
| `refresh --channel stable` | The same manifest, signed again; a manifest expires 45 days after it is signed. |
| `revoke --channel stable --release-key <release.pub.pem> --backup-key <backup.key.pem>` | The release key is revoked: PCs trust the backup key alone until a build with another release key runs. Signed with the backup key, or refused. |
| `status` | What each channel offers. |

A version that fails on a PC is never taken there again: its service served nothing within 120 polls, or its second run ended without stopping within 10 minutes.

## Trying it on this PC

```powershell
pwsh scripts/updates.ps1 keys --out <keys>
pwsh scripts/updates.ps1 serve --folder <channels>
pwsh scripts/install.ps1 -UpdatesFrom http://127.0.0.1:8765/v1/ -UpdatesKeys <keys>
pwsh scripts/updates.ps1 pack --version 0.9.1 --out <zip> --updates-from http://127.0.0.1:8765/v1/ --updates-keys <keys>
pwsh scripts/updates.ps1 publish --folder <channels> --key <keys>/release.key.pem --channel stable --version 0.9.1 --zip x64=<zip> --base-url http://127.0.0.1:8765/v1/ --urgent
```

The service writes each step to the Application event log under `thermi-sensors`.

`pack --msi <msi>` also builds the version's MSI, which installs what `scripts/install.ps1` does, for every user; `msiexec /i <msi> /qn` installs it silently, and Installed apps uninstalls it.

## What the owner sets up

A release needs accounts and keys only the owner can hold. Each step, once, in this order:

1. **The channel's address.** A domain or subdomain for the update server, such as `updates.<your domain>`. Every release build carries `https://<it>/v1/` as `ThermiUpdatesFrom`, so it never changes once a release is out: it goes to the GitHub environment below as the variable `THERMI_UPDATES_FROM`.
2. **The update server.** A Cloudflare R2 bucket, `thermi-updates`, public for reading under that domain, with a cache rule of 60 s for `/v1/*.json`. An R2 API token that writes only that bucket goes to the GitHub environment below as `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY`.
3. **The manifest keys.** On your own PC, `pwsh scripts/updates.ps1 keys --out <folder>`. `release.key.pem` goes to the GitHub environment as the secret `THERMI_UPDATES_KEY`, then off the PC. `backup.key.pem` never goes online: two offline copies, such as a password manager and a USB key; it is only for `revoke`. The text of `release.pub.pem` and `backup.pub.pem` goes to the environment as the variables `THERMI_UPDATES_RELEASE_PUB` and `THERMI_UPDATES_BACKUP_PUB`, which every build carries.
4. **Authenticode.** Apply to SignPath Foundation's free signing for open source with the GitHub repo. In SignPath: a project `thermi` linked to the repo; three artifact configurations, `binaries` (every `.exe` and `.dll` in the zip of a version's folder), `catalog` (`thermi.cat`) and `msi`; and a signing policy `release` that signs only for this repo's `release` environment, whose approval is yours. Its API token goes to the environment as the secret `SIGNPATH_API_TOKEN`, and its organization id as the variable `SIGNPATH_ORGANIZATION_ID`. The certificate's subject name, as Windows shows it under a signed file's Digital Signatures, is the publisher every release build accepts: it goes to the environment as `THERMI_UPDATES_PUBLISHER`, exactly.
5. **The GitHub environment.** `release`, with you as its required reviewer and only `main` allowed to deploy to it, holding the secrets above. Every publish to beta, promotion to stable and monthly signing again waits for your approval there.

The MSI is built with WiX 5, which needs no account or fee.

`.github/workflows/release.yml` then does the rest. Each merge to main goes to beta as `<version>-beta.<run>`. Run it by hand with `release` to put `Directory.Build.props`' version on stable at the rollout given, with its MSIs on a GitHub release; with `promote`, `rollback`, `disable` or `refresh` to pull that lever on a channel. On the first of each month it signs both channels again, before their manifests expire. Every run that signs waits for your approval, and `scripts/channel-sync.ps1` moves the channels between the run and the bucket, the zips before the manifests. `revoke` never runs there: it needs the backup key, so run it on your PC and upload with `scripts/channel-sync.ps1 up`.
