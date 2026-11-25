# Thermi.Trust

What thermi checks on Windows before it runs or changes anything as SYSTEM, or installs anything:

- that a folder is a real one only administrators, SYSTEM and Windows' installer may change, and a new folder made with that access from its first moment (`AdminFolder.cs`);
- that Windows holds a file's Authenticode signature valid, and from whom (`Authenticode.cs`);
- that a release's catalog, `thermi.cat`, lists every file of its version under its own path with its own hash (`FileCatalog.cs`);
- that an update's `switch.lock` is held by a process now, not left on disk by a cut, as both the sensor host and the overlay tell it (`LockFile.cs`).

The overlay's elevated mode installs the driver from such a folder once its installer's signature holds, and the service keeps its updates in one and takes a version only when one of thermi's certificates, by its thumbprint, signed its catalog (`docs/adr/0008-updates.md`). It depends on nothing of thermi's.
