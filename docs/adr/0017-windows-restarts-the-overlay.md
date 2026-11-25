# 0017. Windows restarts an overlay that crashed or hung

**Status**: Accepted

## Context

The overlay is meant to be always there, and nothing brings it back once it ends: the Run key starts it only at sign-in, and the sensor service's own restarts (`sc.exe failure`, set by `scripts/install.ps1`) are for services. An error that escapes the overlay is logged and ends the process (`ThermiLog.RunLoggedAsync`), and a hung overlay that Windows closes as not responding ends it too, so a person finds the overlay gone until they start it again.

A second `thermi` started while one runs never makes a second overlay: it finds the overlay's mutex and opens the settings window instead (R151). Whatever starts the overlay again must not start it beside one already running, or the window opens by itself.

## Decision

- The overlay process, and only it, registers with Windows Error Reporting's `RegisterApplicationRestart` once it holds the overlay's mutex (`src/Thermi.Windows/Win32/CrashRestart.cs`). `thermi settings`, `preview`, `cpu-load` and the elevated mode never register.
- It is restarted with the command line it ran with: none, or `--trace` when it traced, so a traced overlay that crashed traces again. Windows adds the executable itself.
- Windows restarts it after an unhandled error and after a hang that ended it, but only once it had run 60 s, which Windows enforces: an overlay that fails as it starts is not started over and over.
- It passes `RESTART_NO_PATCH` and `RESTART_NO_REBOOT`: it is not restarted when an installer closes it to update thermi, since `scripts/install.ps1` starts the installed overlay itself, nor when Windows restarts for an update, since the Run key starts it at sign-in. Either restart would be a second thermi, which opens the window.
- A registration Windows refuses is logged as a warning, and the overlay runs on without it.

## Rejected

- **A watchdog process that starts the overlay again**: a second always-on process, with its own memory and its own way to fail, to do what Windows already does for any registered program, its 60 s guard against loops included.
- **The sensor service starting the overlay**: it runs as SYSTEM in session 0, and starting a program on the user's desktop from there needs the user's token and a session to pick, for one program of the user's own.

## Consequences

- A crash or hang that took a minute or more to come no longer leaves the person without the overlay; the log still says why it ended.
- Windows asks before it restarts a crashed or hung program, as Error Reporting does; it never restarts one the person ended themselves, as from Task Manager, and "Exit thermi" ends it as any program ends.
- What to watch: an overlay that crashes every few minutes now comes back each time, so the log, not the overlay's absence, is what shows it.
