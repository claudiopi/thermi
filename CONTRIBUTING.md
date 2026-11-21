# Contributing

Thanks for helping. thermi is small on purpose, and so is this guide.

1. Install PowerShell 7 and the .NET SDK version in `global.json`. You do not need admin rights to build or test.
2. Read [AGENTS.md](AGENTS.md): the map, the commands and the rules. It is written for people too.
3. For anything bigger than a small fix, open an issue first so we agree on the approach.
4. One change per PR. The title follows [Conventional Commits](https://www.conventionalcommits.org) (`fix: ...`, `feat: ...`), because it becomes the commit message.
5. `pwsh scripts/check.ps1` passes before you open the PR. The CI runs the same script.

PRs written with AI tools are welcome, as long as you ran the change, understand it, and can explain it.

Bug reports: use the bug template, and include your hardware. Sensor bugs depend on it.
