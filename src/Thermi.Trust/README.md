# Thermi.Trust

What thermi checks with Windows before it runs or changes anything as SYSTEM, or installs anything. The overlay's elevated mode installs the driver only from such a folder once its installer's signature holds, and the service takes an update only when thermi's certificate signed its catalog (`docs/adr/0008-updates.md`). It depends on nothing of thermi's.

- `AdminFolder.cs`: a folder only administrators, SYSTEM and Windows' installer may change, made that way from its first moment.
- `Authenticode.cs`: whether Windows holds a file's signature valid, and from whom.
- `FileCatalog.cs`: whether a release's `thermi.cat` lists every file of its version with its own hash.
- `LockFile.cs`: whether an update's `switch.lock` is held by a running process, not left behind by a power cut.
