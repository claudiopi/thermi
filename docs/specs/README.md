# Specs

Every feature, and every change to what thermi does, starts with a spec: a contract that a person approves before any code is written. It says what must be true when the work is done, and each of those truths is checked by a test or, when only a person can check it, by hand.

Bug fixes, dependency updates and docs need no spec: a bug fix starts with a test that reproduces the bug (`docs/BUGS.md`).

## The folder

`NNN-slug/` in this folder, numbered in order, with three files. Copy the shape of an existing spec.

- **`spec.md`**
  - **Status**: one of `draft`, `approved`, `done`, `dropped`, on the line `**Status**: ...`.
  - **Budget**: the most test cases the tests its acceptance steps name may hold, on the line `**Budget**: <n> .NET and <n> page cases` under the status, set as the person approves the spec. A row counts as a case. Going past it means merging rows and cutting what another case proves, or asking that person to raise it.
  - **Intro**: under the budget, three to five lines on what the feature does and why.
  - **Requirements**: `- R1. ...`, one thing each, testable, no "should", grouped under a heading per topic. An id is never reused or renumbered, since acceptance steps cite it.
  - **Design**: which projects and files hold it, and one alternative that was rejected, with why.
  - All of it says what the feature does today, in the present tense; how it got there is in git's history.
- **`tasks.md`**: while the spec is built, the work in blocks (`B1`, `B2`, ...), each task `T1`, `T2`, ... citing the requirements it covers and ticked when it is closed, with the progress and the current block in the header. Once it is built, only its status and what stays open, one line each that could be an issue, citing the acceptance steps it would make pass.
- **`acceptance.json`**: the steps that prove the spec. Each step has an `id` (`A1`, ...), the requirements it `covers`, a `description` (given, when, then) that names the kinds of case its test proves, not the test's rows, so that a row another proves goes without changing what the person approved: a "table of" lists at most five kinds, a `check` (the .NET test method or the Playwright test title that proves it, one PascalCase word either way, or `by hand`) and `passes`. A `draft` or `dropped` spec may name tests that do not exist.

## The flow

1. `draft`: the agent or person writes the three files. No code yet.
2. `approved`: a person read it and agreed. The acceptance tests are written first, before any other code, and wait with `[Fact(Skip = "Waiting for its code")]`, or `test.fixme` in Playwright, until the code that makes them pass lands. The acceptance steps do not change without that person.
3. `done`: every step `passes`. Tests prove theirs in `scripts/check.ps1`; the person runs the `by hand` ones.

`dropped` keeps the record of a spec that was abandoned, with a line saying why.

`RepoRulesTests` checks that every spec has a known status, that every requirement is covered by some acceptance step, that every check of an approved spec names a test that exists, a .NET test under `tests` or a Playwright test in a `*.spec.ts` file of `src/Thermi.Settings.Web`, that a `done` spec has every step passing, and that no step of a test lists more than five cases of its table. `scripts/test-count.ps1` holds each spec to its budget.
