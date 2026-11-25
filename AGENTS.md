# thermi

A light, always-on-top hardware monitor for Windows: CPU, GPU, RAM and fans (FPS later), in a small overlay you place anywhere on screen. AI agents will be able to configure it through a CLI and MCP.

This is the one guide for people and AI agents alike. Keep it short. New here? Read `docs/QUICKSTART.md`, then `docs/ARCHITECTURE.md`.

## Map

| Path | What lives there |
| --- | --- |
| `src/Thermi.Core` | Everything that does not need Windows: readings, sensor mapping, config and layout. No dependencies. |
| `src/Thermi` | The overlay app, as the user: the overlay, painting, the pipe client, and the settings window that hosts the page in WebView2. |
| `src/Thermi.Settings.Web` | The settings window's page: React and Fluent, holding no logic, talking to C# only through the bridge. |
| `src/Thermi.Settings.Types` | Writes the settings window page's TypeScript from Core in the build: the bridge's types and the texts' keys; and the agents' JSON Schemas. |
| `src/Thermi.Client` | The .NET SDK of the agents' contract, which starts `thermi-cli`: `net8.0`, MIT, no dependencies. |
| `src/Thermi.SensorHost` | The `thermi-sensors` service, the only one that loads LibreHardwareMonitor, and the `thermi-elevated` service, which runs the window's two fixes as SYSTEM. |
| `tests/Thermi.Tests` | Behavior tests, recorded sensor data in `Recordings`, and the repo rules. |
| `tests/Thermi.Windows.Tests` | Tests that need Windows, like the sensor pipe and the window's side of the bridge. |
| `src/Thermi.Settings.Web/tests` | The page's Playwright tests, with axe. |
| `docs/QUICKSTART.md`, `docs/ARCHITECTURE.md` | What thermi is, as a story, and how it is built, with the real names. |
| `docs/agents` | The contract `thermi-cli` answers AI agents and other programs, with its JSON Schemas. |
| `docs/specs` | What each feature must do, with its acceptance checks. Read the spec before working on a feature. |
| `docs/adr` | Why things are the way they are. Read before changing architecture. |

## Commands

Requires the .NET SDK in `global.json` and PowerShell 7.

- `pwsh scripts/check.ps1`: restore, format, the third-party notices, the test count, Release build, tests and coverage, then the settings page's type check, lint, react-doctor, unit tests, dead code (knip) and Playwright tests with axe. The CI runs the same script, each half in a job of its own (`-Part DotNet`, `-Part Web`). Run it before every PR.
- `pwsh scripts/check.ps1 -Mutation`: the same, then Stryker.NET's mutation score of `Thermi.Core`'s tests and StrykerJS's of the page's logic, which measure them better than coverage. Slow: run it when you change Core, the page's logic or their tests. `pwsh scripts/mutation.ps1 --disable-bail` runs Stryker.NET alone and lists every test that kills each mutant.
- `pwsh scripts/smoke-window.ps1 [-Exe <thermi.exe>]`: open the real settings window (the installed one by default) with WebView2's debugging port, click through every screen, and fail on any error the page shows or logs. Run it before a person gets a build; close any open settings window first.
- `pwsh scripts/measure.ps1 [-Window] [-Exe <thermi.exe>] [-Seconds <n>]`: measure for 60 s what the overlay, the settings window with its WebView2 processes (`-Window` opens it and closes it after) and the sensor service cost, CPU as a percent of one core and private memory, and fail past their budgets: the overlay 48 MB and 0.5 %, the idle window with its page's renderer 1 %, the service 80 MB and 3 %. Run it on the installed build after a change to what runs each second; a plain build compiles its code as it runs, so it measures high.
- `pwsh scripts/oracle.ps1`: after a change to what C# answers the page's edits, undos or looks, or to a page test's calls, run them through the real bridge into the fake's table, `src/Thermi.Settings.Web/tests/oracle/edits.json`; `-Prune` drops what no test asks any longer. A page test that asks the table for what it lacks fails, and so does `BridgeOracleTests` when an answer is stale.
- `pwsh scripts/mark-icon.ps1`: draw the program's icon, `src/Thermi/thermi.ico`, from thermi's mark again, after changing the mark.
- `pwsh scripts/notices.ps1`: write `THIRD-PARTY-NOTICES.md` again from the lock files, after adding, removing or updating a package.
- `dotnet test --project tests/Thermi.Tests -- --filter-method "*Threshold*"`: run only some tests while you work.
- `dotnet format whitespace thermi.slnx`: fix formatting.
- `pwsh scripts/install.ps1` (as yourself; it asks for admin rights once): install this build on your PC. `-Uninstall` removes it.
- `thermi-sensors --dump <file>`: save what the sensors read now, to report a bug or record test data. As administrator it sees everything; without, it records the no-admin case.
- `thermi-sensors --dump <file> --seconds <n>`: record a series, one snapshot per second. For the fans' answer to a CPU load, run `thermi-sensors --dump <file> --seconds 45` as administrator, wait 5 s, run `Start-Process -Wait thermi -ArgumentList cpu-load,30` (a windowed program, like `preview`), and let the recording finish.
- `thermi preview <snapshot> <png> [scale] [settings]`: paint the first overlay for a saved snapshot without showing it, or for the last of a saved series with its graphs drawing the series, at a scale from 0.25 to 8, with your settings or those of the file given. It is a windowed program: in PowerShell, wrap it in `Start-Process -Wait`; what it refuses shows in the terminal.
- `thermi settings [--overlay <n>]`: open the settings window on overlay n, counted from 1, or bring the open one to the front. `--pseudo` makes its texts 40 % longer, to see what a long translation does; `--trace`, on the window or the overlay, adds each paint, input and the window's first frame to its log, `%LOCALAPPDATA%\thermi\thermi.log` for the overlay and `thermi-window.log` for the window, where errors always go.

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
| No WPF: the settings window is a web page in WebView2, so no process of thermi's loads WPF. | `RepoRulesTests` |
| Every project has a `README.md` saying what lives there, beyond its title. | `RepoRulesTests` |
| `Thermi.Core` keeps 90 % line and branch coverage. | `scripts/check.ps1` |
| A new field of the settings goes into `src/Thermi.Core/Editing/ConfigFields.cs`, or undo cannot take it back. | `UndoRulesTests` |
| Time comes from an injected `TimeProvider`: no real clock, sleeps, delays or timers without it. A Win32 timer (`SetTimer`) may only wake a window's loop, and what it then shows reads the injected clock. No blocking on tasks. | `BannedSymbols.txt`, VSTHRD002 |
| Every class logs through an injected `ILogger`, each event a `[LoggerMessage]`; no static log, `Trace` or `Debug`. | `BannedSymbols.txt`, CA1848, CA2254 |
| Win32 calls are generated by CsWin32 from `NativeMethods.txt`, never declared by hand. | `RepoRulesTests` |
| No test shows a window or starts a process on the user's desktop: windows are made under `TestWindows.OnOwnDesktop`, processes start through `DesktopProcess`, both on a desktop of the tests' own. | `RepoRulesTests` |
| No TODOs in code: open an issue. | MA0026 |
| Comments say why, never what. No process history (issue numbers, "for now") in production code. | `RepoRulesTests`, comment-trimmer |
| Docs only cite paths that exist. No absolute local paths anywhere. Build files never point outside the repo. | `RepoRulesTests` |
| A rule is turned off only in `Thermi.globalconfig`, with the reason above it. In code, a suppression says why on the same line. NuGet warnings are never suppressed: update the package. | `RepoRulesTests` |
| Package versions live in `Directory.Packages.props`; lock files are committed; packages come only from nuget.org. | NuGet, `nuget.config`, `--locked-mode` in CI |
| `thermi-cli` never elevates, starts a service or installs anything. | `RepoRulesTests` |
| Every answer of the agents' contract validates against its schema, and a schema only grows. | `ContractTests` |
| `THIRD-PARTY-NOTICES.md` lists every package that ships, NuGet and npm, with its license. | `scripts/notices.ps1 -Check` in `scripts/check.ps1` |
| thermi's version lives only in `Directory.Build.props`, and About shows it. | `BridgeContractTests` |
| Every agent brief pins its model. | `RepoRulesTests` |
| Every text a user reads is a member of an enum marked `[Translated]`, and every translation has every text of the English one, with the same blanks, and no others. None is blank or names the driver's product. | `RepoRulesTests` |
| A spec is a folder with `spec.md`, `tasks.md` and `acceptance.json`. Every requirement has an acceptance step, every check is a real test (once approved) or `by hand`, and `done` means every step passes. | `RepoRulesTests` |
| PR titles follow Conventional Commits (they become the squash commit). | CI `pr-title` |
| One behavior, one test, at the cheapest layer that proves it. Its first test is written red at the unit's edge, as its acceptance step. | the ratchet: `scripts/test-count.ps1` holds the count of cases (each `[Fact]`, each row of a `[Theory]`, each Playwright and Vitest case) to `tests/test-count.json`, each half on its own, raised only in a change of its own |
| Near-identical cases are one table-driven test; a bug's reproduction is a new row when the behavior has its test. | the ratchet |
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

Everything in the repo is in English, except the translations themselves in `src/Thermi.Core/Translations`.
