# 001. The overlay

**Status**: done

**Budget**: 59 .NET and 0 page cases

A service reads the PC's sensors with administrator rights from boot, and an overlay, running as the user, shows them in a strip above the taskbar or anywhere on screen, refreshed every second. thermi began as the one program to replace two monitoring tools and the scheduled tasks that kept them above the taskbar, so it has to be trusted all day: a value it cannot read shows as missing, nothing it reads can crash it, and it costs next to nothing at rest.

## Requirements

### Sensors

- R1. A service reads the sensors with admin rights from boot, without asking for them at sign-in, and comes back on its own if it fails.
- R8. Nothing the overlay reads (sensor pipe, settings file) can crash it, and no other local user can feed it numbers.

### What the overlay shows

- R2. An overlay, running as the user, shows by default the CPU's temperature, fan speed, load and power, the GPU's load and the RAM used, refreshed every second.
- R3. Each meter turns yellow and red at configurable thresholds.
- R6. A value that cannot be read shows as missing (`–`): never as zero, and never as an old number that looks live.
- R12. A ping item, under Network, shows the round trip in milliseconds of the last ICMP echo to the settings' host, or `1.1.1.1` with none. No reply within a second, a host that cannot be reached, a name that is not found, or no network shows as missing, and its tooltip, the catalog and an agent say which. It has no lines until the person adds them, since a good ping depends on how far the host is; an added line warns at 100 ms and turns critical at 150 ms.
  Release check: Ping against Windows' ping.
- R13. thermi sends no echo unless an item of an overlay reads the ping. Then each of the user's processes that shows readings sends one a second, and the sensor service never does (`docs/adr/0020-ping.md`). Until an echo ends, and once none has for two seconds, the ping waits, and a call that reads once says what its first echo did.

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
| `src/Thermi.App` | `PingReader`, the ping each process of the user's sends while an item reads it, which its tests drive with a fake echo. |
| `src/Thermi.Windows` | The overlay: a layered Win32 window painted with GDI+, kept above the taskbar as `docs/adr/0004-above-the-taskbar.md` says. |
| `scripts/install.ps1` | Builds as the user, then installs and registers the service with one UAC prompt. |

Rejected: a WPF window for the overlay. It costs about 60 MB and a slower start for something that only paints a strip of text.

## Acceptance

The steps are in `acceptance.json`; what stays open is in `tasks.md`.
