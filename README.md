# thermi app

A light, always-on-top hardware monitor for Windows. CPU, GPU, RAM and fans in a small overlay you can place anywhere: over the taskbar, in a corner, or floating. thermi will suggest designs that fit the spot you pick.

It is built to be configured by AI agents too: a CLI and an MCP server will let your agent inspect your screen, preview a design and apply it.

**Status: early development.** Nothing to download yet.

## Planned

- **0.1**: the overlay with CPU, GPU, RAM and fans, replacing the tools its author uses every day.
- **0.5**: the CLI (`inspect`, `preview`, `apply`, `revert`), the MCP server, design suggestions and the settings window.
- **1.0**: FPS (through PresentMon, never injecting into games), per-app profiles, a signed installer and winget.

## Build from source

Requires Windows, PowerShell 7 and the .NET SDK version in `global.json`.

```
pwsh scripts/check.ps1
```

To run it, install the [PawnIO](https://pawnio.eu) driver, then run this as yourself (not as administrator). It asks for admin rights once, to register the `thermi-sensors` service:

```
pwsh scripts/install.ps1
```

## Contributing

Start with [thermi in ten minutes](docs/QUICKSTART.md) and the [architecture](docs/ARCHITECTURE.md), then read [CONTRIBUTING.md](CONTRIBUTING.md). The rules fit on one page: [AGENTS.md](AGENTS.md).

## Built with AI, in the open

thermi is built by one person with AI coding agents. [How this was built](docs/how-this-was-built.md) explains the setup and the lessons behind it.

## License

[MIT](LICENSE). The libraries thermi ships and their licenses are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
