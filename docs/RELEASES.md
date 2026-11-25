# Releases and updates

How a version of thermi reaches PCs, and the levers on it. Why it works this way is in `docs/adr/0008-updates.md`; what it must do is in `docs/specs/004-updates/spec.md`.

## The channels

The update server is static files: each channel's signed envelope, `stable.json` and `beta.json`, with a 60-second cache, and the zips under `zips/<version>/`, which never change once written. Each installed service checks its channel a minute after it starts and every 4 hours, takes a higher version its rollout reaches, unpacks it beside the running one, and switches at a quiet moment. Nothing about the PC is sent.

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

`pack --msi <msi>` also builds the version's MSIs: `<msi>`, which installs what `scripts/install.ps1` does, for every user, and beside it the same name ending in `-user.msi`, which installs thermi for the user alone with no prompt and no driver (`docs/adr/0016-admin-on-demand.md`). `msiexec /i <msi> /qn` installs either silently, and Installed apps uninstalls it.

## What the owner sets up

A release needs accounts and keys only the owner can hold. Each step, once, in this order:

1. **The channel's address.** A domain or subdomain for the update server, such as `updates.<your domain>`. Every release build carries `https://<it>/v1/` as `ThermiUpdatesFrom`, so it never changes once a release is out: it goes to the repository's variables as `THERMI_UPDATES_FROM`. The build, which signs nothing and so waits for no approval, reads the public values there, and fails when one is missing rather than build a release that never updates.
2. **The update server.** A Cloudflare R2 bucket, `thermi-updates`, public for reading under that domain, with a cache rule of 60 s for `/v1/*.json`. An R2 API token that writes only that bucket goes to the GitHub environment below as `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY`.
3. **The manifest keys.** On your own PC, `pwsh scripts/updates.ps1 keys --out <folder>`. `release.key.pem` goes to the GitHub environment as the secret `THERMI_UPDATES_KEY`, then off the PC. `backup.key.pem` never goes online: two offline copies, such as a password manager and a USB key; it is only for `revoke`. The text of `release.pub.pem` and `backup.pub.pem` goes to the repository's variables as `THERMI_UPDATES_RELEASE_PUB` and `THERMI_UPDATES_BACKUP_PUB`: every build carries them, and every lever opens the channels' signed histories with them, so whoever can write the bucket cannot have a lever sign what they planted. On your PC, the levers take the same folder as `--trust <folder>`.
4. **Authenticode.** Apply to SignPath Foundation's free signing for open source with the GitHub repo. In SignPath: a project `thermi` linked to the repo; three artifact configurations, `binaries` (every `.exe` and `.dll` in the zip of a version's folder), `catalog` (`thermi.cat`) and `msi` (both MSIs of a version, per machine and per user); and a signing policy `release` that signs only for this repo's `release` environment, whose approval is yours. Its API token goes to the environment as the secret `SIGNPATH_API_TOKEN`, and its organization id as the variable `SIGNPATH_ORGANIZATION_ID`. The certificate's SHA-256 thumbprint, as `(Get-AuthenticodeSignature <signed file>).SignerCertificate.GetCertHashString('SHA256')` shows it, is what every release build accepts: it goes to the repository's variables as `THERMI_UPDATES_SIGNERS`. A thumbprint rather than a name, as SignPath Foundation's certificate carries the same name for every project it signs; ask SignPath whether the certificate itself is thermi's own or shared by its projects, since a shared one means the catalog check proves only that SignPath signed the files, and a stolen manifest key is then enough. When the certificate is renewed, list both, separated by a semicolon, in a release made before the old one's last day; a PC that skips that release refuses every later one, until the MSI of a later version is installed on it.
5. **The GitHub environment.** `release`, with you as its required reviewer and only `main` allowed to deploy to it, holding the secrets above and `SIGNPATH_ORGANIZATION_ID`. Every publish to beta, promotion to stable and monthly signing again waits for your approval there.

The MSI is built with WiX 5, which needs no account or fee.

`.github/workflows/release.yml` then does the rest. Each merge to main goes to beta as `<version>-beta.<run>`. Run it by hand with `release` to put `Directory.Build.props`' version on stable at the rollout given, with its MSIs on a GitHub release; with `promote`, `rollback`, `disable` or `refresh` to pull that lever on a channel. On the first of each month it signs both channels again, before their manifests expire. Each job that signs waits for your approval: a beta asks twice, to sign and to publish, and a release three times, its MSIs too. `scripts/channel-sync.ps1` moves the channels between the run and the bucket, the zips before the manifests, and puts each manifest and history only if the bucket still holds what the run fetched, so of two runs at once the second fails and is run again. Right after a stable release, raise `Directory.Build.props`' version: a beta of the version released is older than it, so the channel refuses it. `revoke` never runs there: it needs the backup key, so on your PC run `scripts/channel-sync.ps1 down`, then `revoke`, then `scripts/channel-sync.ps1 up`.

If the release key is stolen: revoke it on both channels as above. Every manifest after a revocation carries it, for the PCs that missed one, and takes only the backup key's signature, so the workflow's levers refuse to sign until you clear it; until then, each lever runs on your PC with the backup key, the monthly `refresh` too, or the manifests expire.

1. Run `keys --out <a new, empty folder>`: it never writes over a key, and you keep only its `release` pair. The backup pair you have stays the one every PC trusts.
2. Put the new private key in `THERMI_UPDATES_KEY` and its public one in `THERMI_UPDATES_RELEASE_PUB`, and merge: the build carries the new key. The run's publish refuses, since the channel is revoked.
3. Download the run's `zip-*` artifacts, and its `msi-*` ones for a stable release. On your PC run `scripts/channel-sync.ps1 down -Version <version>`, so a zip of that version already there with other bytes is refused, then `publish` with `--key <backup.key.pem>`, then `scripts/channel-sync.ps1 up`. For stable, make the GitHub release with its MSIs: `gh release create v<version> <the MSIs>`.
4. Once the manifest lifetime, 45 days, has passed, run `unrevoke` on each channel on your PC with the backup key, between `down` and `up`.

A PC offline through all of it still trusts the stolen key, and needs the MSI of a later version.
