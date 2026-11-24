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
