# 0013. One stalled device never holds back the others' readings

**Status**: Proposed

## Context

The service reads every device in one pass each second (`LhmSensorSource`), and only sends a snapshot once the pass ends. In QA round 4, under Find it's load, the service's snapshots stopped for 3 to 5.5 s several times while a plain sampler at normal priority kept ticking: the stall was the service's own, not the PC's. The strong suspect is another LibreHardwareMonitor-based program, such as FanControl, writing fan speeds often under load and holding the Super I/O chip's bus mutex (`Global\Access_ISABUS.HTP.Method`), which every such program shares. The board's update then waits for the mutex, and with it every other device's reading, so the overlay goes blank and Find it loses the seconds it needs.

## Decision

- Each device, a board's Super I/O chip as one of its own, is updated in turn, as before, but on a thread of its own, and the pass waits for it at most 500 ms. A device still updating then is left to finish, and the pass goes on with the next: one stalled device holds back none after it.
- A device still updating is not started again until its update returns. Meanwhile its sensors keep their last values while that update is no older than the freshness limit the overlay applies to a whole snapshot (3 s, `LatestSnapshot.MaxAge`), and after that are sent as missing (null), so the catalog never changes for a stall and no frozen value looks live.
- An update that takes longer than 500 ms is written to the Application event log once it returns, with the device and how long it took, at most once a minute per device, so a busy bus shows without flooding the log. In a terminal, as with `--console` or `--dump`, it is written there instead.
- While an update is still running, the library is neither scanned again for new devices nor closed: either would pull the devices from under it.
- A device whose update has not returned within the read limit (60 s, `SensorPipeServer.ReadLimit`) ends the process, as a whole read past it did: only a process that ends frees a thread stuck in a driver, and Windows' recovery restarts the service.

## Rejected

- **Every device on a thread of its own, all at once**: the library updates its devices one after another, and several share drivers and buses; updating them all in parallel would trade one stall for races the library was never built for. Here devices overlap only with one that is stalled, which mostly waits on a mutex.
- **Sending each sensor's age on the pipe**: the client already treats a whole snapshot past 3 s as gone; a per-sensor age would change the wire (ADR 0012) for what nulls already say.

## Consequences

- With the bus held by another program, the CPU, GPU, memory and drives keep coming each second, and only the board's sensors go missing after 3 s.
- A read becomes asynchronous (`ISensorSource.ReadAsync`), and the pipe's server awaits it.
- What to watch: the event log's slow-update lines, to tell a busy bus from a driver that hangs.
- A device that fails its update, as before, is left out of the snapshot; one that is only slow keeps its sensors, as values or as missing.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md`: "It reads sensors only while a client is connected, once per second" keeps its words, but a device stalled past 500 ms in a read is left to finish while the others are read and sent.
