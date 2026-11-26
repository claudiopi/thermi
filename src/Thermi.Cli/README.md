# Thermi.Cli

`thermi-cli`'s commands and its MCP server, which serves the same commands over stdio (`docs/adr/0013-agents-cli-and-mcp.md`, the contract in `docs/agents/README.md`). Each command reads the sensors and the settings file, lets Core decide the answer (`src/Thermi.Core/Agents`) and prints one JSON document, or a line a second for `watch`. A command that writes asks Core's `AgentRules` first, as each write does again over the file as it then finds it: while the person lets agents only read, in the window's Settings, AI agents, it is refused and writes nothing. Each change is logged with Core's `AgentActivity.Message`, which the window reads back to list the last ones.

It targets plain `net10.0` and references no Windows-only project (`RepoRulesTests`): what a command reaches outside thermi's own logic, the sensors' feed, whether the service is installed, whether an overlay runs, the start at sign-in, the monitors, comes in through `Agents/IAgentEnvironment.cs`, which each OS's host answers, as `src/Thermi.Windows/Agents/AgentEnvironment.cs` does. The host's executable runs it: on Windows, `thermi-cli.exe`, a console apphost of `thermi.dll`. Its types are internal, seen by the hosts and the tests its project names.

| Folder | What lives there |
| --- | --- |
| `Commands` | The agents' command line: each command and its arguments, and the parse a host adds its own commands to. |
| `Agents` | Running a command or the MCP server: reads, edits and their undo, looks, the fan finder, the start at sign-in, over `IAgentEnvironment`. |

Its tests are in `tests/Thermi.Tests`, where `tests/Thermi.Tests/Agents/AgentRig.cs` runs a command as a host does; what only the built `thermi-cli.exe` or the real pipe shows is in `tests/Thermi.Windows.Tests`.
