# Thermi.Sensors.MacOS

`thermi-sensors` on macOS, the sensor service (`docs/adr/0021-macos.md`). It needs no driver and no root: it reads the SMC, the HID sensors, IOReport's energy, the GPU's statistics and the Mach and BSD counters as the person, once a second while a client listens, and writes Core's snapshots with `SnapshotWriter` to the socket launchd holds for it, `~/Library/Application Support/thermi/sensors.sock`. It runs as a LaunchAgent of the person's, in their background session, so it serves their desktop and their SSH sessions alike, and maps nothing itself: Core's `SensorMap` reads Apple's names.

It targets plain `net10.0`, marked for macOS in `Program.cs`, so it builds and replays its recordings on any OS. Its tests are in `tests/Thermi.MacOS.Tests`.
