# Thermi.Tests

Behavior tests for `src/Thermi.Core`, and the repo rules. They run anywhere, without hardware or admin rights.

- `Recordings` holds snapshots from real PCs, made with `thermi-sensors --dump`, or read from the service's pipe for what only the running service does, as its scan for new hardware: the author's Ryzen with admin rights and without, and an Intel desktop's with them, named by CPU and GPU. A new recording is the way to reproduce a sensor bug. A `.jsonl` file is a series, one snapshot per second, for code that follows values over time; it keeps only the sensors its tests follow, so it stays small. A snapshot is read with `tests/Thermi.Windows.Tests/TestRecordings.cs`, shared rather than copied, and a series with `TestFiles.cs`.
- `Configs` holds settings files as each version wrote them, so every version keeps reading.
- `PlacementTests.cs` checks how a snap names its monitor from its EDID.
- `Rules/RepoRulesTests.cs` enforces the rules of `AGENTS.md` that a compiler cannot: file sizes, folder sizes, comments, cited paths, specs and more. `Rules/UndoRulesTests.cs` checks that undo can take back every field of the settings.
- `Acceptance` holds the tests a spec's acceptance steps name.

Name tests after the behavior they prove, as they read in a spec's acceptance steps.
