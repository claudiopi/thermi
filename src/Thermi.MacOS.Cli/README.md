# Thermi.MacOS.Cli

`thermi-cli` on macOS (`docs/adr/0021-macos.md`). This build answers no command yet; the agents' commands over the Mac's socket, settings and agents are in `docs/specs/008-macos/spec.md`. It is a project of its own, apart from `thermi`, so it never loads AppKit: it is to answer on a Mac reached only over SSH, which no one ever signed in to graphically.

| Folder | What lives there |
| --- | --- |
| `Agents` | The Mac's environment for the agents' commands, and the two LaunchAgents that `thermi register` writes and loads, which `thermi` builds in too. |
| `Native` | The one call declared by hand, libSystem's `geteuid`: the person this process acts as. |
| `Sensors` | The service's socket, read only when the person serves it. |

It targets plain `net10.0`, marked for macOS in `Program.cs`. Its tests, from zsh, bash and SSH, are in `tests/Thermi.MacOS.Tests`.
