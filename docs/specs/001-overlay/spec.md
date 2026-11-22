# 001. The overlay replaces the tools its author runs every day

**Status**: done

## Why

On the author's PC, two tools and three scheduled tasks do what thermi should do alone: one shows CPU, GPU and RAM over the taskbar, another shows the CPU fan, and a script keeps re-raising them above the taskbar. Version 0.1 replaces all of it, and has to be trusted all day.

## Requirements

- R1. A service reads the sensors with admin rights from boot, without asking for them at sign-in, and comes back on its own if it fails.
- R2. An overlay, running as the user, shows the CPU fan (speed, with a meter for its duty), the CPU (temperature, load, power), the GPU load and the RAM used (with a meter for its load), refreshed every second.
- R3. Each meter turns yellow and red at configurable thresholds.
- R4. The overlay stays above the taskbar without any other program's help.
- R5. It is placed by dragging and stays where it was put, across restarts and monitor changes.
- R6. A value that cannot be read shows as missing (`–`): never as zero, and never as an old number that looks live.
- R7. Settings live in a JSON file the user can edit; changes apply at once, and a broken file is reported and never overwritten.
- R8. Nothing the overlay reads (sensor pipe, config file) can crash it, and no other local user can feed it numbers.
- R9. At rest the overlay uses under 0.5 % of one core and 40 MB of private memory.

## Design

- `src/Thermi.SensorHost`: the `thermi-sensors` service (`docs/adr/0003-sensor-service.md`).
- `src/Thermi.Core`: sensor mapping, config, layout and the pipe's line format, all tested without Windows.
- `src/Thermi`: the overlay, a layered Win32 window painted with GDI+, staying above the taskbar as `docs/adr/0004-above-the-taskbar.md` says.
- `scripts/install.ps1`: builds as the user, then installs and registers the service with one UAC prompt.
- Rejected: a WPF window for the overlay. It costs about 60 MB and a slower start for something that only paints a strip of text; WPF waits for the settings window.

## Acceptance

In `acceptance.json`. The work is in `tasks.md`.
