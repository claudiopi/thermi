# Thermi.Tests

Behavior tests for `src/Thermi.Core`, and the repo rules. They run anywhere, without hardware or admin rights.

- `Recordings` holds snapshots from real PCs, made with `thermi-sensors --dump`: one with admin rights and one without. A new recording is the way to reproduce a sensor bug.
- `Configs` holds settings files as each version wrote them, so every version keeps reading.
- `RepoRulesTests.cs` enforces the rules of `AGENTS.md` that a compiler cannot: file sizes, comments, cited paths, specs and more.

Name tests after the behavior they prove, as they read in a spec's acceptance steps.
