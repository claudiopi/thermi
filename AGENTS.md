# thermi

A light, always-on-top hardware monitor for Windows: CPU, GPU, RAM, fans, voltages and the FPS of the game in front, in a small overlay you place anywhere on screen. AI agents read it and configure it through a CLI and MCP.

This is the one guide for people and AI agents alike. Keep it short. New here? Read `docs/QUICKSTART.md`, then `docs/ARCHITECTURE.md`.

## Map

| Path | What lives there |
| --- | --- |
| `src/Thermi.Core` | Everything that does not need Windows: readings, sensor mapping, config and layout. No dependencies. |
| `src/Thermi.App` | What the overlay and the window share across OSes beyond Core, needing a package or the file system: the settings file and the settings a host holds. `net10.0`. |
| `src/Thermi.Cli` | `thermi-cli`'s commands and its MCP server, the same commands over stdio, for any OS: what an OS answers comes in through `IAgentEnvironment`. `net10.0`. |
| `src/Thermi.Windows` | The overlay app, as the user: the overlay, painting, the pipe client, and the settings window that hosts the page in WebView2. |
| `src/Thermi.Settings.Web` | The settings window's page: React and Fluent, holding no logic, talking to C# only through the bridge. |
| `src/Thermi.Settings.Types` | Writes the settings window page's TypeScript from Core in the build: the bridge's types and the texts' keys; and the agents' JSON Schemas. |
| `src/Thermi.Client` | The .NET SDK of the agents' contract, which starts `thermi-cli`: `net10.0`, MIT, no dependencies. |
| `src/Thermi.Sensors.Windows` | The `thermi-sensors` service, the only one that loads LibreHardwareMonitor and the one that updates thermi; the same host as the user's own process in a per-user install, which updates it and installs it for everyone; and the `thermi-elevated` service, which runs the window's two fixes as SYSTEM. |
| `src/Thermi.Trust` | Windows' own checks both the window's fixes and the updater trust: a folder only administrators change, Authenticode signatures, signed catalogs and a held switch lock. |
| `src/Thermi.Releases` | `thermi-releases`, the owner's levers on the update channels, which sign each channel's manifest. |
| `src/Thermi.Installer` | The MSI, built with WiX 5 from a published version. |
| `tests/Thermi.Tests` | Behavior tests, recorded sensor data in `Recordings`, and the repo rules. |
| `tests/Thermi.Windows.Tests` | Tests that need Windows, like the sensor pipe and the window's side of the bridge. |
| `src/Thermi.Settings.Web/tests` | The page's Playwright tests, with axe. |
| `docs/QUICKSTART.md`, `docs/ARCHITECTURE.md` | What thermi is, as a story, and how it is built, with the real names. |
| `docs/agents` | The contract `thermi-cli` answers AI agents and other programs, with its JSON Schemas. |
| `docs/BUGS.md` | How a bug goes from a report to a fix. |
| `docs/specs` | What each feature must do, with its acceptance checks. Read the spec before working on a feature. |
| `docs/adr` | Why things are the way they are. Read before changing architecture. |

## Commands

Requires the .NET SDK in `global.json` and PowerShell 7.

| Command | What it does, and when |
| --- | --- |
| `pwsh scripts/check.ps1` | The one check, the CI's too: restore, format, build, notices, test count, tests and coverage, then the page's type check, lint, unit, dead-code and Playwright tests. Run it before every PR. `-Part DotNet` or `-Part Web` runs one half. Set `THERMI_WEB_PORT` to another port than 4173 to check two worktrees at once. |
| `pwsh scripts/check.ps1 -Mutation` | The same, then the mutation scores of Core's tests and the page's logic, each with the rows that kill and run nothing of their own (`scripts/redundant.ps1`). Slow: run it when you change Core, the page's logic or their tests. |
| `pwsh scripts/test.ps1 --project tests/Thermi.Tests --filter-method "*Threshold*"` | Runs only some tests while you work. `-Web tests/Quality.spec.ts -g "fits"` builds the page and runs some Playwright tests. |
| `dotnet format whitespace thermi.slnx` | Fixes formatting. |
| `pwsh scripts/smoke-window.ps1 [-Exe <thermi.exe>]` | Clicks through every screen of the real settings window and fails on any error the page shows or logs. Run it before a person gets a build, with no settings window open. |
| `pwsh scripts/measure.ps1 [-Window] [-Exe <thermi.exe>]` | Measures the overlay, the window and the service for 60 s and fails past their budgets: overlay 48 MB and 0.5 % of a core, idle window 1 %, service 80 MB and 3 %. Run it on the installed build after a change to what runs each second. |
| `pwsh scripts/oracle.ps1` | After a change to what C# answers the page's edits, undos or looks, records the real bridge's answers into `src/Thermi.Settings.Web/tests/oracle/edits.json`. `-Prune` drops what no test asks. |
| `pwsh scripts/doc-impact.ps1 [-Base <branch>]` | Lists the docs that name a file or type this change touched, for the reviewer to read. |
| `pwsh scripts/notices.ps1` | Writes `THIRD-PARTY-NOTICES.md` again, after adding, removing or updating a package. |
| `pwsh scripts/mark-icon.ps1` | Draws the program's icon, `src/Thermi.Windows/thermi.ico`, after changing thermi's mark. |
| `pwsh scripts/install.ps1` | Installs this build on your PC, as yourself; it asks for admin rights once. `-SensorsOnly` installs the sensors and `thermi-cli` alone; `-Uninstall` removes it. |
| `pwsh scripts/updates.ps1 <command>` | Packs a version and its MSIs, makes keys, and pulls the update channels' levers: `docs/RELEASES.md`. |
| `thermi-sensors --dump <file> [--seconds <n>]` | Saves what the sensors read now, or a series of one snapshot a second, to report a bug or record test data. As administrator it sees everything; without, it records the no-admin case. |
| `thermi-sensors --frames <file> --seconds <n>` | As administrator, records every present for n seconds, to replay frames in tests. It never records a process's name. |
| `thermi preview <snapshot> <png> [scale] [settings]` | Paints the first overlay for a saved snapshot, at a scale from 0.25 to 8, without showing it. In PowerShell, wrap it in `Start-Process -Wait`. |
| `thermi settings [--overlay <n>]` | Opens the settings window on overlay n, from 1. `--pseudo` makes its texts 40 % longer; `--trace` logs each paint and input to `%LOCALAPPDATA%\thermi`. |

To record how the fans answer a CPU load: as administrator run `thermi-sensors --dump <file> --seconds 45`, wait 5 s, run `Start-Process -Wait thermi -ArgumentList cpu-load,30`, and let the recording finish.

## Rules and what enforces them

A rule without a check gets forgotten. If you add a rule, add its check in the same change.

| Rule | Enforced by |
| --- | --- |
| Warnings are errors, nullable is on, analyzers are strict. | `Directory.Build.props`, `Thermi.globalconfig` |
| Methods up to 40 lines, cognitive complexity up to 15, nesting up to 3, up to 7 parameters. | Sonar, `SonarLint.xml` |
| Code files up to 400 lines. | `RepoRulesTests` |
| Code lines up to 160 characters (generated `.g.ts` aside). | `RepoRulesTests` |
| Code lives in folders by topic, at most 12 C# files directly in any folder, and a namespace follows its folder. | `RepoRulesTests`, IDE0130 |
| `Thermi.Core` depends on nothing, Windows included. | its target framework, `RepoRulesTests` |
| What every OS's host shares, `Thermi.Core`, `Thermi.App`, `Thermi.Cli` and `Thermi.Client`, targets plain `net10.0` and builds on no Windows-only project. | `RepoRulesTests` |
| No WPF: the settings window is a web page in WebView2, so no process of thermi's loads WPF. | `RepoRulesTests` |
| Every project has a `README.md`: a short intro, and a table of its folders that names every folder with code and none that is gone. | `RepoRulesTests` |
| Docs are short and scannable: the why before the what, short sentences, tables over walls of bullets, and no list of every file, which the code already is. | the README table rule in `RepoRulesTests`; review for the rest |
| `Thermi.Core` keeps 90 % line and branch coverage. | `scripts/check.ps1` |
| A new field of the settings goes into `src/Thermi.Core/Editing/ConfigFields.cs`, or undo cannot take it back. | `UndoRulesTests` |
| Time comes from an injected `TimeProvider`: no real clock, sleeps, delays or timers without it. A Win32 timer (`SetTimer`) may only wake a window's loop, and what it then shows reads the injected clock. No blocking on tasks. | `BannedSymbols.txt`, VSTHRD002 |
| Every class logs through an injected `ILogger`, each event a `[LoggerMessage]`; no static log, `Trace` or `Debug`. | `BannedSymbols.txt`, CA1848, CA2254 |
| Win32 calls are generated by CsWin32 from `NativeMethods.txt`, never declared by hand. | `RepoRulesTests` |
| No test shows a window or starts a process on the user's desktop: windows are made under `TestWindows.OnOwnDesktop`, processes start through `DesktopProcess`, both on a desktop of the tests' own. | `RepoRulesTests` |
| No test writes to Windows' Event Log: only the sinks each program's Main hands down reach it, and no test names one. | `RepoRulesTests` |
| No TODOs in code: open an issue. | MA0026 |
| Comments say why, never what. No process history (issue numbers, "for now") in production code. | `RepoRulesTests`, comment-trimmer |
| Docs only cite paths that exist, and code and its comments only repo paths and project files that exist. No absolute local paths anywhere. Build files never point outside the repo. | `RepoRulesTests` |
| A doc names only what the code has: each type, member and file in backticks, and each participant and call of a Mermaid diagram, a member only where its type declares it. ADRs and specs keep the names of their day. | `RepoRulesTests` |
| What can be listed from the code is checked against it: `thermi.slnx` holds every project, this map has a row for each, and `docs/agents/README.md` lists every command `thermi-cli` parses. | `RepoRulesTests` |
| A change's reviewer reads every doc `scripts/doc-impact.ps1` lists for it and says which the change made false. Docs say why and how things flow; the detail of each file is left to the code, so less can rot. | `.claude/agents/reviewer.md` |
| A rule is turned off only in `Thermi.globalconfig`, with the reason above it. In code, a suppression says why on the same line. NuGet warnings are never suppressed: update the package. | `RepoRulesTests` |
| Package versions live in `Directory.Packages.props`; lock files are committed; packages come only from nuget.org. | NuGet, `nuget.config`, `--locked-mode` in CI |
| `thermi-cli` never elevates, starts a service or installs anything. | `RepoRulesTests` |
| Every answer of the agents' contract validates against its schema, and a schema only grows. | `ContractTests` |
| `THIRD-PARTY-NOTICES.md` lists every package that ships, NuGet and npm, with its license, and each license's text with every copyright line. | `scripts/notices.ps1 -Check` in `scripts/check.ps1` |
| thermi's version lives only in `Directory.Build.props`, and About shows it. | `BridgeContractTests` |
| Every agent brief pins its model. | `RepoRulesTests` |
| Every text a user reads is a member of an enum marked `[Translated]`, and every translation has every text of the English one, with the same blanks, and no others. None is blank or names the driver's product. | `RepoRulesTests` |
| A spec is a folder with `spec.md`, `tasks.md` and `acceptance.json`. Every requirement has an acceptance step, every check is a real test (once approved) or `by hand`, and `done` means every step passes. | `RepoRulesTests` |
| PR titles follow Conventional Commits (they become the squash commit). | CI `pr-title` |
| One behavior, one test, at the cheapest layer that proves it. Its first test is written red at the unit's edge, as its acceptance step. | the ratchet: `scripts/test-count.ps1` holds the count of cases (each `[Fact]`, each row of a `[Theory]`, each Playwright and Vitest case) to `tests/test-count.json`, each half on its own, raised only in a change of its own, which in a pull request is compared with main's: main holds the file before a branch raises it |
| Near-identical cases are one table-driven test; a bug's reproduction is a new row when the behavior has its test. | the ratchet |
| An acceptance step names the kinds of case its test proves, never the test's rows: its "table of" lists at most five. | `RepoRulesTests` |
| Each spec's `spec.md` declares its budget, `**Budget**: <n> .NET and <n> page cases`, and the tests its steps name hold no more cases. | `scripts/test-count.ps1` |
| A table holds at most 17 rows, and a row kills or runs a mutant no other case does. A table whose rows are each a behavior of their own past that, or that Stryker cannot judge (an operand of `&&` or `\|\|`, a number at a boundary, a static table's entry, a bug), says why on its `[Theory]` line, or on or above the line that opens a page's loop or `.each`, as `// Rows: ...`. | `scripts/test-count.ps1`; `scripts/redundant.ps1` in `scripts/check.ps1 -Mutation`, which fails past 3 % of the cases judged |
| Inner tests only for pure logic with real decisions. A test that drove a small step goes at the refactor once an edge test covers it and no new mutant survives without it. Characterization tests and oracles go when their spec passes. | the ratchet, `scripts/mutation.ps1` |
| The page's fake never decides what C# decides: an edit, an undo, a gesture, a saved look or a search is answered from the real bridge's answers to the same calls. | `BridgeOracleTests`, the `oracle` fixture of `src/Thermi.Settings.Web/tests/oracle.ts` |

These need judgment, so review enforces them:

- The edges are recorded sensor data in and what the overlay shows out, never one test per class.
- No test asserts a UI text, the DOM's structure or that something was called.
- Fakes are never kinder than real hardware: missing sensors, NaN, frozen values, no driver, no admin rights.
- A bug fix starts with a test that reproduces the bug, red before the fix.
- When you replace something, delete the old one in the same change. No dead code, no stale docs.
- The second copy of the same logic is when you extract it.
- Anything touching drivers, admin rights or architecture needs an ADR first.

## Working with AI agents

0. A feature starts with a spec that a person approves before any code: `docs/specs/README.md`.
1. Implement, with tests. The spec's acceptance tests are written first.
2. Prune the comments of the change: `.claude/agents/comment-trimmer.md`.
3. `pwsh scripts/check.ps1` passes.
4. A different agent than the author reviews, once per round: `.claude/agents/reviewer.md`. With a tool other than Claude Code, paste that brief into a fresh session, ideally on another model.
5. A person tests it and merges. Agents commit, push and merge only when a person asks, and never with `--admin`.

A bug follows the same loop from a report and a red test, with no spec: `docs/BUGS.md`.

Everything in the repo is in English, except the translations themselves in `src/Thermi.Core/Translations`.
