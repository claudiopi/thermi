# Thermi.Sensors.Linux

`thermi-sensors` on Linux, the sensor service (`docs/adr/0019-linux.md`). It needs no driver: it reads the kernel's `hwmon`, NVIDIA's NVML when present, and `/proc` and `/sys`, once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to `/run/thermi/sensors.sock`. It runs as the system user `thermi` with no capability, in a sandboxed systemd unit that its socket starts, and maps nothing itself: Core's `SensorMap` reads the kernel's names.

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, from recorded trees of `/sys` and `/proc`, are in `tests/Thermi.Linux.Tests`.
