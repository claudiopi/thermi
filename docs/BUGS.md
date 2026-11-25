# Bugs

How a bug goes from a report to a fix on your PC, for people and agents alike. Bug fixes need no spec: the red test is the contract.

1. **Report.** An issue from the bug template (`.github/ISSUE_TEMPLATE/bug.yml`): thermi's and Windows' versions, the hardware, what was expected and what was seen, a screenshot, `thermi-sensors --dump` as administrator and not, and the logs. Security bugs go privately, as `SECURITY.md` says.
2. **Triage.** A person or an agent gives it the labels below, and asks for whatever the report lacks.
3. **Reproduce.** The dump becomes a recording in `tests/Thermi.Tests/Recordings`, with anything personal taken out, such as a device's serial. The first commit is a test at the edge that fails on it: a new row when the behavior already has its test.
4. **Fix.** On a branch `fix/<issue>-<slug>`, then the loop of `AGENTS.md`: the comment-trimmer, `pwsh scripts/check.ps1`, the reviewer. The PR says `Fixes #<issue>` and what the person will see once it ships.
5. **Verify.** A person checks the fix on an installed build before it merges, and the release notes name it.

| Label | Means |
| --- | --- |
| `bug` | Something thermi does wrong. The template sets it. |
| `severity: crash` | thermi, the window or the service ends or hangs. |
| `severity: wrong value` | A value is wrong, missing, or old but shown as live. |
| `severity: cosmetic` | It looks wrong, and nothing else is. |
| `area: overlay`, `area: window`, `area: sensors`, `area: fps`, `area: agents`, `area: updates`, `area: install` | Where it lives. |
