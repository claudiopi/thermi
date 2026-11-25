# thermi

A light, always-on-top hardware monitor for Windows. CPU, GPU, RAM and fans in a small overlay you place anywhere: over the taskbar, in a corner, or floating.

**Status: v1 preview, 0.9.0.** There is no release to download yet: you build it from source and install it on your own PC. Expect rough edges, and please report them.

## What it does

- Shows the values you pick, such as temperatures, loads, power, clocks, memory and fan speeds, in small pills, and keeps them on top, the taskbar included.
- Colors a value at its warning and critical thresholds, and shows `–` for one it cannot read, never a zero or an old number that looks live.
- A settings window, "Settings…" in the overlay's menu or `thermi settings`, picks the values and their sensors, the look, the position, the thresholds and the hotkeys, with undo. English and Spanish.
- Reads the sensors in a small Windows service, `thermi-sensors`, so the overlay and the window never run as administrator.
- CPU temperature, power and clocks need a small signed driver. When they read nothing, the settings window offers to install it, behind one Windows permission prompt.
- AI agents and other programs read the sensors and change the settings with `thermi-cli`, from any shell or as an MCP server, with or without an overlay: [thermi for agents](docs/agents/README.md). For Claude Code, `claude mcp add thermi -- thermi-cli mcp`.

Planned: FPS, and a signed installer.

## Install from source

Requires Windows 11 on x64, with PowerShell 7, the .NET SDK version in `global.json`, and Node.js 22.12 or later for the settings window's page. Windows 10 22H2 and Arm64 PCs should work but are untested.

Run this as yourself, not as administrator:

```
pwsh scripts/install.ps1
```

It builds thermi, then asks Windows for administrator rights once, to copy it to Program Files and register the `thermi-sensors` service. It installs the WebView2 runtime the settings window needs only where it is missing, puts `thermi-cli` on the `PATH`, and starts thermi at sign-in. Run it again to update. `-SensorsOnly` installs the sensors and `thermi-cli` alone, with the driver, and no overlay. `pwsh scripts/install.ps1 -Uninstall` removes thermi but keeps your settings, and leaves the driver, which other programs may use: remove it from Installed apps if nothing else needs it.

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
