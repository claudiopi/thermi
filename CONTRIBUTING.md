# Contributing

Thanks for helping. thermi has many rules, but you do not need to learn them first: most have a check, and a failing check mostly says how to fix it. Your first PR can be small, and a maintainer helps with the rest.

The full rulebook is [AGENTS.md](AGENTS.md), written for people and AI agents alike. This page is the short way in.

## Your first PR in 10 minutes

The easiest real change is in `src/Thermi.Core`, which holds everything thermi decides, with its test in `tests/Thermi.Tests`. Core targets plain `net10.0` and depends on nothing, so it builds on Windows, Linux and macOS.

| Step | What to do |
| --- | --- |
| 1. Tools | The .NET SDK version in `global.json` and PowerShell 7. You do not need admin rights. |
| 2. A task | An issue labeled [good first issue](https://github.com/claudiopi/thermi/labels/good%20first%20issue), or any small fix. For anything bigger, open an issue first so we agree on the approach. |
| 3. The test first | Write the test in `tests/Thermi.Tests`, next to the tests of the code you change, and watch it fail. If the behavior already has a test, add a row to its `[Theory]` instead of a new test. |
| 4. The change | Change `src/Thermi.Core` until the test passes. |
| 5. Run your tests | Windows or Linux: `pwsh scripts/test.ps1 --project tests/Thermi.Tests --filter-class "*YourTests"`. macOS: `dotnet test --project tests/Thermi.Tests --filter-class "*YourTests"`. |
| 6. Check | On Windows: `pwsh scripts/check.ps1 -Part Core`, which runs the format, the build, the test count and every test of `tests/Thermi.Tests` with Core's coverage. It also builds the settings page, which needs Node.js 22.12 or later. On Linux: `pwsh scripts/check.ps1 -Part LinuxCore`, which builds and runs every test of `tests/Thermi.Tests`; the CI runs the format, the test count and the coverage. On macOS, skip this step: the CI runs it. |
| 7. The PR | One change per PR. The title follows [Conventional Commits](https://www.conventionalcommits.org) (`fix: ...`, `feat: ...`), because it becomes the commit message. |

A new feature, as opposed to a fix, starts with a spec that a person approves: [docs/specs/README.md](docs/specs/README.md).

## What the CI runs for you

Every PR runs all of `scripts/check.ps1` on Windows and on Linux, whatever you ran yourself, and the page's tests on Linux beside it. A PR merges only when its one required check, `CI gate`, passes ([docs/RELEASES.md](docs/RELEASES.md)).

| Job | What it checks | Runs on |
| --- | --- | --- |
| Core | Restore, format, build, the third-party notices, the test count, `tests/Thermi.Tests` and Core's coverage | Windows; you can run most of it anywhere |
| Windows tests | `tests/Thermi.Windows.Tests`: the overlay, the sensor pipe, the settings window | Windows |
| Thermi.Tests on Linux | `tests/Thermi.Tests` again, as Linux's .NET reads paths, cultures and files its own way | Linux |
| Linux tests | `tests/Thermi.Linux.Tests`: the sensor service, its socket, the overlay against headless desktops, and the packages installed in containers | Linux |
| Settings page | The page's build, lint, react-doctor, unit tests, dead code, the GNOME extension's lint, and Playwright with axe in Edge | Windows |
| Settings page on Linux | The page's Playwright tests in WebKit and Chromium | Linux |
| Installers | The MSIs for x64 and Arm64 | Windows |
| PR title, Security | The title's format, secrets and vulnerable packages | Either: none of it runs thermi |

Without Windows or Linux you can still open the PR: the CI runs what needs each, and its failures show in the run's summary.

## The rules that trip people

| Rule | When it fails | How to fix it |
| --- | --- | --- |
| The test count only goes up in a commit of its own, which a person approves. | `test count` | Commit your work, then run `pwsh scripts/test-count.ps1 -Raise -Part DotNet` on Windows after a Release build: it commits the new count alone. For the page's tests, `-Part Web`. Without Windows, write the count the CI's failure names into `tests/test-count.json`, in a commit of its own. |
| Formatting follows `.editorconfig`. | `format` | `dotnet format whitespace thermi.slnx` |
| `THIRD-PARTY-NOTICES.md` lists every package that ships. | `third-party notices` | After you add, remove or update a package, run `pwsh scripts/notices.ps1`. |
| Comments say why, never what, and hold no history such as issue numbers. | `RepoRulesTests`, review | Delete what the code already says. `.claude/agents/comment-trimmer.md` says what to keep. |
| A code file has at most 400 lines, and a line at most 160 characters. | `RepoRulesTests` | Split a file by topic; break a line, or a long string over several. |
| Every text a person reads is a member of a `[Translated]` enum, in English and Spanish. | `RepoRulesTests` | Add the member, then its text to `src/Thermi.Core/Translations/Texts.resx` and `Texts.es.resx`. No Spanish? Write your best try and say so in the PR. |
| One behavior, one test, at the cheapest layer that proves it. | review, the test count | A new case of a tested behavior is a new row of its table. |
| A new field of the settings is listed in `src/Thermi.Core/Editing/ConfigFields.cs`, or undo cannot take it back. | `UndoRulesTests` | Add it there. |
| Time comes from an injected `TimeProvider`, and logs from an injected `ILogger`. | the build | Take them in the constructor, as the classes beside yours do. |

Every other rule, and what checks it, is in [AGENTS.md](AGENTS.md).

## Good first issues

Issues labeled `good first issue` are small tasks in `src/Thermi.Core`: each names the behavior, the file that decides it, and how a test proves it, often from a recording of a real PC in `tests/Thermi.Tests/Recordings`. To suggest one, use the [Small Core task](https://github.com/claudiopi/thermi/issues/new?template=good-first-issue.yml) template.

## AI tools and bug reports

PRs written with AI tools are welcome, as long as you ran the change, understand it, and can explain it.

Bug reports: use the bug template, and include your hardware, since sensor bugs depend on it. How a bug gets fixed: [docs/BUGS.md](docs/BUGS.md).
