# Thermi.Tests

Behavior tests for `src/Thermi.Core` and for `thermi-cli`'s commands and MCP server in `src/Thermi.Cli`, and the repo rules. They run anywhere, without hardware or admin rights: recorded sensor data goes in, and what the overlay, the window or an agent would show comes out.

| Folder | What lives there |
| --- | --- |
| `Recordings` | Snapshots from real PCs, made with `thermi-sensors --dump`, named by CPU and GPU. A `.jsonl` file is a series, one snapshot a second. A new recording is how a sensor bug is reproduced. |
| `Configs` | Settings files as each version wrote them, so every version keeps reading. |
| `Golden` | The pipe's lines byte for byte, as the service writes them. |
| `Acceptance` | The tests a spec's acceptance steps name. |
| `Agents` | The rig that runs `thermi-cli`'s commands, as a host runs them, and an MCP session, on a settings file and a recorded service of the test's own. |
| `Bridge` | The page's bridge, through its real wire. |
| `Logging` | What the overlay and the window write on the standard error with `--trace`, where a test reads it. |
| `Contract` | The agents' contract: every answer against its schema, and schema 1 as it shipped. |
| `Rules` | The rules of `AGENTS.md` that no compiler checks: sizes, comments, paths, names in docs, specs, undo. |

Entry points worth knowing:

- `TestFiles.cs` reads a series and finds the repo's root; a single snapshot is read with `tests/Thermi.Windows.Tests/TestRecordings.cs`, and a settings file of the test's own is `tests/Thermi.Windows.Tests/SettingsFolder.cs`, both shared rather than copied.
- `Rules/RepoRulesTests.cs` is where a new repo rule gets its check.

Name a test after the behavior it proves, as it reads in a spec's acceptance steps.
