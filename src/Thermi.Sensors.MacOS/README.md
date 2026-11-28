# Thermi.Sensors.MacOS

`thermi-sensors` on macOS, the sensor service (`docs/adr/0021-macos.md`). It needs no driver and no root: it reads the SMC, the HID sensors, IOReport's energy, the GPU's statistics and the Mach and BSD counters as the person, once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to the socket launchd holds for it, `~/Library/Application Support/thermi/sensors.sock`. It runs as a LaunchAgent of the person's, in their background session, so it serves their desktop and their SSH sessions alike, and maps nothing itself: Core's `SensorMap` reads Apple's names.

Every reader reads an `IMac`, the Mac's answers of one second: the Mac itself, or a recording of it, so the readers replay on any OS, and `--raw` records what they asked while `--dump` saves the snapshots they made of it.

| Folder | What lives there |
| --- | --- |
| `Native` | The calls declared by hand into IOKit's SMC, HID sensors and registry, IOReport, CoreFoundation, sysctl and Mach. |
| `Source` | `IMac`: the Mac live, a recording being made of it, or one replayed in its place, and how a recording is kept. |
| `Reading` | Each device's readings: the SMC's keys of the chip, the CPU, the GPU, memory, drives, network and battery, named as LibreHardwareMonitor names them. |
| `Serving` | The command line, and the socket: the one launchd hands it, or one of the person's own with `--console`, served by Core's `SocketServer`. |

It targets plain `net10.0`, marked for macOS in `Program.cs`, so it builds and replays its recordings on any OS. Its tests are in `tests/Thermi.MacOS.Tests`.
