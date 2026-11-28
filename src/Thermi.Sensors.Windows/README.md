# Thermi.Sensors.Windows

`thermi-sensors.exe`, the sensor service. Reading CPU and board sensors needs admin rights, so this is the only process that has them and the only one that loads LibreHardwareMonitor (`docs/adr/0003-sensor-service.md`). In a machine install it runs as SYSTEM; in a per-user one, `thermi-sensors --user` runs as the user, reads what the user's rights allow, and logs to `%LOCALAPPDATA%\thermi\thermi-sensors.log` (`docs/adr/0016-admin-on-demand.md`). Either way it reads once a second while someone listens, and sends snapshots down a read-only pipe. It also keeps thermi up to date.

Fewer dependencies in a SYSTEM process is a security property, so it takes no logging library: it writes to the Application event log, or in a per-user install to its own file (`docs/adr/0006-proven-libraries-for-logging-and-win32.md`).

| Folder | What lives there |
| --- | --- |
| `Reading` | LibreHardwareMonitor, each device updated on its own thread, so one that stalls holds back none (`docs/adr/0015-a-stalled-device-holds-back-none.md`), and what the service mends before it sends. |
| `Pipe` | The `thermi-sensors` pipe, whose clients it takes in and hands to Core's `SnapshotBroadcast`, which sends the whole snapshot once, then the values alone (`docs/adr/0012-sensor-pipe-catalog-once.md`). |
| `Service` | The command line, what the service reaches outside its code (which tests replace), and the `thermi-elevated` service that runs the window's two fixes. |
| `Frames` | The frames games present, from ETW (`docs/adr/0014-frames-from-etw.md`). |
| `Updates` | The updater and the watchdog that switches versions and rolls back (`docs/adr/0008-updates.md`). |
| `Install` | `--adopt` and `--remove`: lay a version down or take thermi off, for the MSI and `scripts/install.ps1`. |
| `User` | `--user`: the sensors as the user when no service is installed (`docs/adr/0016-admin-on-demand.md`). |

Entry points worth knowing:

- `Program.cs` reads the arguments and runs the service, a console, a dump, a watchdog or an install.
- `Reading/LhmSensorSource.cs` is the only class that talks to LibreHardwareMonitor.
- `Pipe/SensorPipeServer.cs` takes in each client of the pipe and hands it to Core's `SnapshotBroadcast`, which writes each snapshot to every client and ends the process when a read hangs past 60 s, so Windows restarts it.
- `Updates/Updater.cs` checks the channel and takes a version only when its signatures hold.

Its tests are in `tests/Thermi.Windows.Tests/Sensors`.
