# Thermi.MacOS.Cli

`thermi-cli` on macOS (`docs/adr/0021-macos.md`): `Thermi.Cli`'s `AgentRun` over an `IAgentEnvironment` of the Mac's, which reads the service's socket, the settings file under `~/Library/Application Support/thermi` and the agents' plists, and asks the running overlay for its monitors. It is a project of its own, apart from `thermi`, so it never loads AppKit: it answers on a Mac reached only over SSH, which no one ever signed in to graphically.

It targets plain `net10.0`, marked for macOS in `Program.cs`. Its tests, from zsh, bash and SSH, are in `tests/Thermi.MacOS.Tests`.
