# Thermi.Sensors.Linux

`thermi-sensors` on Linux, the sensor service (`docs/adr/0019-linux.md`).

It needs no driver: it reads the kernel's `hwmon`, NVIDIA's NVML when present, and `/proc` and `/sys`, once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to `/run/thermi/sensors.sock`. It runs as the system user `thermi` with no capability, in a sandboxed systemd unit that its socket starts, and maps nothing itself: Core's `SensorMap` reads the kernel's names.

| Folder | What lives there |
| --- | --- |
| `Kernel` | The kernel's files, live, recorded into a tree of one folder a second, or replayed from one, which scrubs what names the PC; and NVML. |
| `Reading` | Each device's readings from the kernel: the CPU, memory, drives, network, battery, GPUs and the board's chips, named as LibreHardwareMonitor names them. |
| `Serving` | The command line, and the socket: the one systemd hands it, or one of the user's own with `--console`, served by Core's `SnapshotBroadcast`. |

Its packaging, beside the code in `Packaging`, is systemd's socket and service units, the user `systemd-sysusers` makes and the udev rule that lets the service read the CPU's energy; `scripts/linux-packages.ps1` packs them with the service and `thermi-cli`.

It targets plain `net10.0`, marked for Linux in `Program.cs`. Its tests, from recorded trees of `/sys` and `/proc`, are in `tests/Thermi.Linux.Tests`.
