# Thermi.Trust

What thermi checks on Windows before it runs or changes anything as SYSTEM: that a folder is a real one only administrators, SYSTEM and Windows' installer may change, and a new folder made with that access from its first moment (`AdminFolder.cs`). The overlay's elevated mode installs the driver from such a folder, and the service keeps its updates in one (`docs/adr/0008-updates.md`). It depends on nothing of thermi's.
