# How this was built

thermi is built by one person working with AI coding agents, in the open. This page explains how, so you can judge the result and reuse what works.

## The setup

- `AGENTS.md` is the only guide, for people and agents alike. Every rule in it names the check that enforces it.
- `scripts/check.ps1` is the only quality check. It runs locally and in CI, so they never disagree.
- Every change goes through the same loop (in `AGENTS.md`): an agent prunes its comments, the check passes, a second agent reviews it with a fixed brief (`.claude/agents/reviewer.md`), and a person tests it on real hardware and merges.
- Agents never merge on their own. The only automatic merges are updates to build tools and CI actions, by Renovate, once the CI passes.
- Every commit made with an agent says which model wrote it, and every PR says what the reviewer agent concluded.

## Lessons we started with

The same person built larger projects this way before. These are the things that cost months to fix there. thermi commits to preventing them from the first commit:

| What went wrong | What thermi does from day one |
| --- | --- |
| Rules written in docs and forgotten. | Each rule has a check that fails the build or the CI. |
| Files of thousands of lines. | Size and complexity limits as build errors. |
| Analyzers turned on late, leaving warnings nobody would ever fix. | Strict analyzers and warnings as errors from the first line. |
| Local hooks and CI applying different rules. | One script for both. |
| Warnings silenced by the hundreds, nobody knew why. | Every suppression says why, and a test checks it. |
| Comments narrating the code, or its history. | A check for history in comments, and an agent that prunes the rest. |
| Fakes kinder than reality, so tests passed and the app failed. | Sensor tests replay real recorded data, failures included. |
| Logic copied many times until the copies disagreed. | The second copy is extracted; the reviewer looks for copies. |
| Old docs that misled the next person. | Replaced things are deleted in the same change; docs can only cite paths that exist. |
| Project state in files that every change touched. | Issues and PRs are the state. |
| Merging before the CI finished. | `main` gets protected, with one required `CI gate` check, before the first PR. |
| Packages from whatever feed a machine had. | Only nuget.org, with committed lock files. |

## What is left out on purpose

Git hooks, agent skills, per-folder rule files, golden image tests, architecture test libraries, a budget file, and setup for agent tools other than Claude Code. Each one gets added only after the problem it solves has shown up twice.
