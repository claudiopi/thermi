# Thermi.Sensors.Linux

`thermi-sensors` on Linux, the sensor service (`docs/adr/0019-linux.md`). Its code is not written yet: `Program.cs` says it reads no sensors and ends with 1, and the tests that will prove it wait, skipped, in `tests/Thermi.Linux.Tests`.

What spec 006 asks of it (`docs/specs/006-linux/spec.md`): it needs no driver. It reads the kernel's `hwmon`, NVIDIA's NVML when present, and `/proc` and `/sys`, once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to `/run/thermi/sensors.sock`. It runs as the system user `thermi` with no capability, in a sandboxed systemd unit that its socket starts, and maps nothing itself: Core's `SensorMap` reads the kernel's names.

It targets plain `net10.0`, marked for Linux in `Program.cs`.
