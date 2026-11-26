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

A release needs accounts and keys only the owner can hold. Do each step once, in this order.

| Step | What | Where it ends up |
| --- | --- | --- |
| 1. The channel's address | A domain for the update server, such as `updates.<your domain>`. Every release build carries `https://<it>/v1/`, so it never changes once a release is out. | The repository's variable `THERMI_UPDATES_FROM`, in step 6. |
| 2. The update server | A Cloudflare R2 bucket, `thermi-updates`, public for reading under that domain, with a 60 s cache rule for `/v1/*.json`, and an API token that writes only that bucket. | The `release` environment's `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`. |
| 3. The manifest keys | `pwsh scripts/updates.ps1 keys --out <folder>` on your own PC. `release.key.pem` goes online, then off the PC. `backup.key.pem` never goes online: keep two offline copies; it is only for `revoke`. | The secret `THERMI_UPDATES_KEY`; the public keys' text as the variables `THERMI_UPDATES_RELEASE_PUB` and `THERMI_UPDATES_BACKUP_PUB`, in step 6. |
| 4. Authenticode | SignPath Foundation's free signing for open source: a project `thermi` linked to the repo, three artifact configurations (`binaries`: every `.exe` and `.dll` of a version's zip; `catalog`: `thermi.cat`; `msi`: both MSIs), and a signing policy `release` that signs only for this repo's `release` environment. | The secret `SIGNPATH_API_TOKEN` and the variable `SIGNPATH_ORGANIZATION_ID`; the certificate's SHA-256 thumbprint as the variable `THERMI_UPDATES_SIGNERS`, in step 6. |
| 5. The GitHub environment | `release`, with you as its required reviewer and only `main` allowed to deploy to it. | Holds the secrets above. Every publish, promotion and monthly signing waits for your approval. |
| 6. The repository's variables | All four at once: `THERMI_UPDATES_FROM`, `THERMI_UPDATES_RELEASE_PUB`, `THERMI_UPDATES_BACKUP_PUB`, `THERMI_UPDATES_SIGNERS`. | From then on every merge runs the release workflow. |
| 7. Branch protection | A ruleset on `main`: changes only through a pull request, squash-merged, with the one required status check `CI gate`, and no one may bypass it. `CI gate` passes only when every job of `.github/workflows/ci.yml` passed: Core's tests and the build's checks, the Windows tests, the Windows tests that run alone, the page, the MSIs, the PR's title, and `.github/workflows/security.yml`'s: no secret in the history, no package with a known vulnerability, and none added by the PR. Branches need not be up to date before merging, or Renovate's auto-merges wait forever. | The repository's settings, under Rules. Jobs can change without touching it. |
| 8. Renovate | The free Mend Renovate app, installed on this repository alone, and Allow auto-merge on, with squash merging the only method allowed. | `renovate.json` drives it: it opens the dependency PRs and auto-merges build tools' and actions' once `CI gate` passes. |

Why some of it is this way:

- Until step 6, a merge skips the release workflow with a notice. With only some variables set, every run fails, rather than build a release that never updates.
- Every lever opens the channels' signed histories with the public keys, so whoever can write the bucket cannot have a lever sign what they planted. On your PC, the levers take the keys' folder as `--trust <folder>`.
- The thumbprint is what `(Get-AuthenticodeSignature <signed file>).SignerCertificate.GetCertHashString('SHA256')` shows. It is a thumbprint, not a name, because SignPath Foundation's certificate carries the same name for every project it signs. Ask SignPath whether the certificate is thermi's own: a shared one means the catalog proves only that SignPath signed the files.
- When the certificate is renewed, list both thumbprints, separated by a semicolon, in a release made before the old one's last day. A PC that skips that release refuses every later one until a later MSI is installed on it.
- The MSI is built with WiX 5, which needs no account or fee.

## The release workflow

`.github/workflows/release.yml` does the rest:

- Each merge to main goes to beta as `<version>-beta.<run>`.
- Run by hand with `release`, it puts `Directory.Build.props`' version on stable at the rollout given, with its MSIs on a GitHub release. With `promote`, `rollback`, `disable` or `refresh` it pulls that lever.
- On the first of each month it signs both channels again, before their manifests expire.
- Each job that signs waits for your approval: a beta asks twice, a release three times.
- `scripts/channel-sync.ps1` moves the channels between the run and the bucket, the zips before the manifests, and writes a manifest only if the bucket still holds what the run fetched. Of two runs at once, the second fails and is run again.
- Right after a stable release, raise `Directory.Build.props`' version: a beta of the version released is older than it, and the channel refuses it.
- `revoke` never runs there, since it needs the backup key: on your PC run `scripts/channel-sync.ps1 down`, then `revoke`, then `scripts/channel-sync.ps1 up`.

## If the release key is stolen

Revoke it on both channels, as above. From then on every manifest takes only the backup key's signature, so the workflow refuses to sign until you clear it; meanwhile run each lever on your PC with the backup key, the monthly `refresh` too, or the manifests expire.

1. Run `keys --out <a new, empty folder>`: it never writes over a key. Keep only its `release` pair; the backup pair stays the one every PC trusts.
2. Put the new private key in `THERMI_UPDATES_KEY` and its public one in `THERMI_UPDATES_RELEASE_PUB`, and merge. The run's publish refuses, since the channel is revoked.
3. Download the run's `zip-*` artifacts, and its `msi-*` ones for stable. On your PC run `scripts/channel-sync.ps1 down -Version <version>`, then `publish` with `--key <backup.key.pem>`, then `scripts/channel-sync.ps1 up`. For stable, `gh release create v<version> <the MSIs>`.
4. After 45 days, the manifests' lifetime, run `unrevoke` on each channel on your PC with the backup key, between `down` and `up`.

A PC offline through all of it still trusts the stolen key, and needs the MSI of a later version.
