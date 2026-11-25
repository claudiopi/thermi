# Specs

Every feature, and every change to what thermi does, starts with a spec: a contract that a person approves before any code is written. It says what must be true when the work is done, and each of those truths is checked by a test or, when only a person can check it, by hand.

Bug fixes, dependency updates and docs need no spec: a bug fix starts with a test that reproduces the bug (`docs/BUGS.md`).

## The folder

`NNN-slug/` in this folder, numbered in order, with three files. Copy the shape of an existing spec.

- **`spec.md`**
  - **Status**: one of `draft`, `approved`, `done`, `dropped`, on the line `**Status**: ...`.
  - **Why**: the problem, in two or three lines.
  - **Requirements**: `- R1. ...`, one thing each, testable, no "should".
  - **Design**: which projects and files change, and one alternative that was rejected, with why.
- **`tasks.md`**: the work in blocks (`B1`, `B2`, ...). Each task `T1`, `T2`, ... cites the requirements it covers and is ticked when it is closed. The header keeps the progress and the current block.
- **`acceptance.json`**: the steps that prove the spec. Each step has an `id` (`A1`, ...), the requirements it `covers`, a `description` (given, when, then), a `check` (the .NET test method or the Playwright test title that proves it, one PascalCase word either way, or `by hand`) and `passes`. A `draft` or `dropped` spec may name tests that do not exist.

## The flow

1. `draft`: the agent or person writes the three files. No code yet.
2. `approved`: a person read it and agreed. The acceptance tests are written first, before any other code, and wait with `[Fact(Skip = "Waiting for its code")]`, or `test.fixme` in Playwright, until the code that makes them pass lands. The acceptance steps do not change without that person.
3. `done`: every step `passes`. Tests prove theirs in `scripts/check.ps1`; the person runs the `by hand` ones.

`dropped` keeps the record of a spec that was abandoned, with a line saying why.

`RepoRulesTests` checks that every spec has a known status, that every requirement is covered by some acceptance step, that every check of an approved spec names a test that exists, a .NET test under `tests` or a Playwright test in a `*.spec.ts` file of `src/Thermi.Settings.Web`, and that a `done` spec has every step passing.
