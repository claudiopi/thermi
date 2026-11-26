# 001. The overlay

**Status**: done

**Budget**: 43 .NET and 0 page cases

A service reads the PC's sensors with administrator rights from boot, and an overlay, running as the user, shows them in a strip above the taskbar or anywhere on screen, refreshed every second. thermi began as the one program to replace two monitoring tools and the scheduled tasks that kept them above the taskbar, so it has to be trusted all day: a value it cannot read shows as missing, nothing it reads can crash it, and it costs next to nothing at rest.

## Requirements

### Sensors

- R1. A service reads the sensors with admin rights from boot, without asking for them at sign-in, and comes back on its own if it fails.
- R8. Nothing the overlay reads (sensor pipe, settings file) can crash it, and no other local user can feed it numbers.

### What the overlay shows

- R2. An overlay, running as the user, shows by default the CPU's temperature, fan speed, load and power, the GPU's load and the RAM used, refreshed every second.
- R3. Each meter turns yellow and red at configurable thresholds.
- R6. A value that cannot be read shows as missing (`–`): never as zero, and never as an old number that looks live.

### Where it stays

- R4. The overlay stays above the taskbar without any other program's help.
- R5. It is placed by dragging and stays where it was put, across restarts and monitor changes.
  Release check: Restart and sign-in.

### Settings and cost

- R7. Settings live in a JSON file the user can edit; changes apply at once, and a broken file is reported and never overwritten.
- R9. At rest the overlay uses under 0.5 % of one core and 48 MB of private memory (`docs/adr/0001-architecture.md`).
  Release check: Costs at rest.

## Design

| Where | What |
| --- | --- |
| `src/Thermi.Sensors.Windows` | The `thermi-sensors` service, which reads the sensors and serves them on a named pipe (`docs/adr/0003-sensor-service.md`). |
| `src/Thermi.Core` | Sensor mapping, settings, layout and the pipe's line format, all tested without Windows. |
| `src/Thermi.Windows` | The overlay: a layered Win32 window painted with GDI+, kept above the taskbar as `docs/adr/0004-above-the-taskbar.md` says. |
| `scripts/install.ps1` | Builds as the user, then installs and registers the service with one UAC prompt. |

Rejected: a WPF window for the overlay. It costs about 60 MB and a slower start for something that only paints a strip of text.

## Acceptance

The steps are in `acceptance.json`; what stays open is in `tasks.md`.
