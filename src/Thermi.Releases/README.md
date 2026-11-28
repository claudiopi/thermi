# Thermi.Releases

`thermi-releases`, the owner's levers on Windows' update channels (`docs/adr/0008-updates.md`), which `scripts/updates.ps1` runs. The same levers on Linux's repositories are `scripts/repository.ps1`'s, which `scripts/updates.ps1 --repository` runs instead. Each lever writes the channel's next signed manifest into a folder laid out as the server is. Core decides every lever (`src/Thermi.Core/Updates/Levers.cs`); this only reads and writes files. Nothing of it ships.

| Lever | Does |
| --- | --- |
| `keys` | Makes the release and backup key pairs. |
| `publish` | Puts a build's zips on a channel, with a rollout and an urgent flag. |
| `promote` | Takes beta's version to stable. |
| `rollback` | Withdraws a channel's version. |
| `disable` | Turns a channel off. |
| `refresh` | Signs a channel again before it expires. |
| `status` | Shows each channel. |
| `serve` | Answers a channels folder over HTTP on this PC, for a test build. |

`Program.cs` reads the levers; `Server.cs` is `serve`.
