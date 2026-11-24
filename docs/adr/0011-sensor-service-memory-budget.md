# 0011. The sensor service's memory budget

**Status**: Accepted

## Context

ADR 0001 set the sensor host's budget at 30 MB of private memory, before any of it was built. It was never met. Measured as administrator on the author's PC (Ryzen 9 9950X, RTX 5060 Ti, one NVMe drive, two adapters), with 30 s samples:

- The installed service, a day up, holds 119 MB private, flat (0.15 MB an hour).
- A build of this change reading drives every second or every 5 s holds 71 to 73 MB, with ~500 handles. Its managed heap is only 10 to 13 MB, while the GC keeps 30 to 35 MB committed: most of the rest is LibreHardwareMonitor's own floor (its drivers' buffers, the runtime and the libraries it loads), which no setting of ours reaches.
- Ten drive updates a second for 25 minutes held 70 MB, the same as one every 5 s: reading SMART and every volume leaks nothing measurable. Each drive update costs about 3 ms of CPU.
- What the GC keeps beyond the heap is its young generation's budget, sized from the processor's cache. A small young generation (`DOTNET_GCgen0size`) brought a build from 71 to 57 MB. That setting is read only from the environment, not from `runtimeconfig.json` (checked: neither `System.GC.Gen0Size` nor `GCgen0size` there changes anything).

Published ReadyToRun as installed, for 25 minutes from minute 10: as it is, 72 MB private on average and rising (67 to 77 MB, GC committed 29 to 36 MB), 30 young collections, 36 s of CPU. With `DOTNET_GCgen0size=200000` in its environment, for 20 minutes: 55 MB private from minute 10 (53 to 57 MB, flat; GC committed 15 to 16 MB, heap 9 to 10 MB), a young collection every 6 s, 2.2 % of one core.

## Decision

- **Budget**: the sensor service stays under 80 MB of private memory at rest, with a 1 second refresh, network and drives read.
- **The GC**: the service runs with a 2 MB young generation, `DOTNET_GCgen0size=200000` (hex). `scripts/install.ps1` writes it as the service's own environment, the `Environment` value of its key under `HKLM\SYSTEM\CurrentControlSet\Services`, as it registers the service; deleting the service deletes it. Nothing else on the PC sees it, and the GC still grows the heap as far as it needs.
- **Drives** are updated every 5 s and send their last values between (`LhmSensorSource.StorageEvery`), for the CPU and the disk requests each update makes.

## Rejected

- **A hard limit on the heap** (`System.GC.HeapHardLimit` at 32 MB, which `runtimeconfig.json` takes): it sizes the young generation from the limit, with the same effect, but any growth of the heap past it (more drives and adapters, a bigger PC) would end the service with an out-of-memory error, again at each restart.
- **Keeping 30 MB**: LibreHardwareMonitor alone needs more, and every other choice of ours would not close the gap.
- **Reading drives every second**: four fifths more drive requests and CPU, for values that change slowly.

## Consequences

- `DOTNET_GCgen0size` is a runtime setting outside the documented ones: a .NET update could drop it. The service would still run, back near 72 MB; measure the service after each .NET update.
- A service run by hand, as `thermi-sensors --dump`, runs without it unless the shell sets it.
- The GC runs a young collection every 6 s instead of every 50 s, at no CPU cost that shows: the same 2 % of one core.
- A drive's rates are 5-second averages, and a drive's values can be up to 5 s old.

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md`, Budget: "the sensor host under 30 MB" becomes "the sensor host under 80 MB", with the young generation above.
