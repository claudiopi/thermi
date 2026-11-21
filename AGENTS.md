# thermi

A light, always-on-top hardware monitor for Windows: CPU, GPU, RAM and fans (FPS later), in a small overlay you place anywhere on screen. AI agents will be able to configure it through a CLI and MCP.

This is the one guide for people and AI agents alike. Keep it short.

## Map

| Path | What lives there |
| --- | --- |
| `src/Thermi.Core` | Everything that does not need Windows: readings, formatting, thresholds, and later layout and config. No dependencies. |
| `tests/Thermi.Tests` | Behavior tests and the repo rules. |
| `docs/adr` | Why things are the way they are. Read before changing architecture. |

The Windows processes (the overlay app and the elevated sensor host) arrive with the first overlay. See `docs/adr/0001-architecture.md`.

## Commands

Requires the .NET SDK in `global.json` and PowerShell 7.

- `pwsh scripts/check.ps1`: restore, format, Release build, tests and coverage. The CI runs the same script. Run it before every PR.
- `dotnet test --project tests/Thermi.Tests -- --filter-method "*Threshold*"`: run only some tests while you work.
- `dotnet format whitespace thermi.slnx`: fix formatting.

## Rules and what enforces them

A rule without a check gets forgotten. If you add a rule, add its check in the same change.

| Rule | Enforced by |
| --- | --- |
| Warnings are errors, nullable is on, analyzers are strict. | `Directory.Build.props`, `Thermi.globalconfig` |
| Methods up to 40 lines, cognitive complexity up to 15, nesting up to 3, up to 7 parameters. | Sonar, `SonarLint.xml` |
| Code files up to 400 lines. | `RepoRulesTests` |
| `Thermi.Core` depends on nothing, Windows included. | its target framework, `RepoRulesTests` |
| `Thermi.Core` keeps 90 % line and branch coverage. | `scripts/check.ps1` |
| Time comes from an injected `TimeProvider`: no real clock, sleeps, delays or timers without it. No blocking on tasks. | `BannedSymbols.txt`, VSTHRD002 |
| No TODOs in code: open an issue. | MA0026 |
| Comments say why, never what. No process history (issue numbers, "for now") in production code. | `RepoRulesTests`, comment-trimmer |
| Docs only cite paths that exist. No absolute local paths anywhere. Build files never point outside the repo. | `RepoRulesTests` |
| A rule is turned off only in `Thermi.globalconfig`, with the reason above it. In code, a suppression says why on the same line. NuGet warnings are never suppressed: update the package. | `RepoRulesTests` |
| Package versions live in `Directory.Packages.props`; lock files are committed; packages come only from nuget.org. | NuGet, `nuget.config`, `--locked-mode` in CI |
| Every agent brief pins its model. | `RepoRulesTests` |
| PR titles follow Conventional Commits (they become the squash commit). | CI `pr-title` |

These need judgment, so review enforces them:

- Test behavior at the edges (recorded sensor data in, what the overlay shows out), not one test per class.
- Fakes are never kinder than real hardware: missing sensors, NaN, frozen values, no driver, no admin rights.
- A bug fix starts with a test that reproduces the bug.
- When you replace something, delete the old one in the same change. No dead code, no stale docs.
- The second copy of the same logic is when you extract it.
- Anything touching drivers, admin rights or architecture needs an ADR first.

## Working with AI agents

1. Implement, with tests.
2. Prune the comments of the change: `.claude/agents/comment-trimmer.md`.
3. `pwsh scripts/check.ps1` passes.
4. A different agent than the author reviews, once per round: `.claude/agents/reviewer.md`. With a tool other than Claude Code, paste that brief into a fresh session, ideally on another model.
5. A person tests it and merges. Agents commit, push and merge only when a person asks, and never with `--admin`.

Everything in the repo is in English.
