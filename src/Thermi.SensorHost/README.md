# Thermi.SensorHost

`thermi-sensors.exe`: the only elevated process and the only one that loads LibreHardwareMonitor (`docs/adr/0003-sensor-service.md`).

- `Program.cs` runs it as the `thermi-sensors` Windows service, as LocalSystem. `--console` runs it in a terminal, as administrator; `--dump <file>` saves one snapshot and exits.
- `LhmSensorSource.cs` reads CPU, GPU, memory, motherboard and controller sensors. Storage and network are never read. One device that fails is skipped; it never silences the rest.
- `SensorPipeServer.cs` serves the `thermi-sensors` pipe: read-only for interactive users, at most 4 clients, a snapshot per second, and a client that stops reading is dropped after 2 s. It reads the sensors only while a client is connected.

A failure stops the service with an error code, so Windows restarts it. Its tests are in `tests/Thermi.Windows.Tests`.
