# thermi

[![CI](https://github.com/claudiopi/thermi/actions/workflows/ci.yml/badge.svg)](https://github.com/claudiopi/thermi/actions/workflows/ci.yml)
[![Mutation](https://github.com/claudiopi/thermi/actions/workflows/mutation.yml/badge.svg)](https://github.com/claudiopi/thermi/actions/workflows/mutation.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A light, always-on-top hardware monitor for Windows. CPU, GPU, RAM, fans, voltages and the FPS of the game in front in a small overlay you place anywhere: over the taskbar, in a corner, or floating.

**Status: v1 preview, 0.9.0.** There is no release to download yet: you build it from source and install it on your own PC. Expect rough edges, and please report them.

## What it does

- Shows the values you pick, such as temperatures, loads, power, clocks, voltages, memory, fan speeds and the game's FPS, in small pills that stay on top, the taskbar included.
- Colors a value at its warning and critical thresholds, and shows `–` for one it cannot read: never a zero, never an old number that looks live.
- A settings window, "Settings…" in the overlay's menu or `thermi settings`, picks the values, the look, the place, the thresholds and the hotkeys, with undo. In English and Spanish.
- Reads the sensors in `thermi-sensors`, so the overlay and the window never run as administrator. Installed for everyone, it is a small Windows service. Installed for one user without admin rights, it runs as that user and reads what those rights allow: no CPU temperature, power or clocks, board fans, drive health or FPS.
- The CPU's temperature, power and clocks need a small signed driver. When they read nothing, the settings window offers to install it, behind one Windows prompt.
- AI agents and other programs read the sensors and change the settings with `thermi-cli`, from any shell or as an MCP server: [thermi for agents](docs/agents/README.md). For Claude Code, `claude mcp add thermi -- thermi-cli mcp`.
- Keeps itself up to date silently, as a browser does. A new version is checked against thermi's signatures and switched to at a quiet moment; one that fails is set aside, and the PC stays on the one that worked.

## Tested on

- **The development PC**: a desktop with a Ryzen 9 9950X and an NVIDIA GeForce RTX 5060 Ti, on Windows 11.
- **Automated tests on every change**, their count held by a ratchet in [`tests/test-count.json`](tests/test-count.json). Each [CI run](https://github.com/claudiopi/thermi/actions/workflows/ci.yml)'s summary shows the counts, the coverage and react-doctor's score. Every night, a [mutation run](https://github.com/claudiopi/thermi/actions/workflows/mutation.yml) measures how many planted bugs the tests catch, and fails below its break.
- **Sensor data recorded on real PCs**, which the tests replay: the PC above with and without admin rights, and an Intel desktop with a Core i9-12900K, in [`tests/Thermi.Tests/Recordings`](tests/Thermi.Tests/Recordings).

Other hardware: see [Hardware reports](https://github.com/claudiopi/thermi/issues/new?template=hardware-report.yml).

## Privacy

thermi sends nothing about you or your PC. Its only connection is the update check, a minute after the service starts and every 4 hours: one HTTPS request for the channel's manifest, with no query, no cookie, no identifier and `thermi` as its whole User-Agent, and then the zip of a new version, whose signature Windows checks against the certificate authority's revocation servers, as it does for any signed program. The server sees the address a request comes from, as any server does. An administrator turns updates off with the `Updates` policy value in `HKLM\SOFTWARE\Policies\thermi`, and a build from source never checks unless it is told where.

## Install from source

Requires Windows 11 on x64, with PowerShell 7, the .NET SDK version in `global.json`, and Node.js 22.12 or later for the settings window's page. Windows 10 22H2 and Arm64 PCs should work but are untested.

Run this as yourself, not as administrator:

```
pwsh scripts/install.ps1
```

It builds thermi, then asks Windows for administrator rights once, to copy it to Program Files and register the `thermi-sensors` service. It also installs WebView2 where it is missing, puts `thermi-cli` on the `PATH`, and starts thermi at sign-in.

- A build from source never updates itself: run the script again to update.
- `-SensorsOnly` installs the sensors and `thermi-cli` alone, with the driver, and no overlay.
- `-Uninstall` removes thermi but keeps your settings. It leaves the driver, which other programs may use; remove it from Installed apps if nothing else needs it.

## Build and test

```
pwsh scripts/check.ps1
```

## Contributing

Start with [thermi in ten minutes](docs/QUICKSTART.md) and the [architecture](docs/ARCHITECTURE.md), then read [CONTRIBUTING.md](CONTRIBUTING.md). The rules fit on one page: [AGENTS.md](AGENTS.md).

## Built with AI, in the open

thermi is built by one person with AI coding agents. [How this was built](docs/how-this-was-built.md) explains the setup and the lessons behind it.

## License

[MIT](LICENSE). The libraries thermi ships and their licenses are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
