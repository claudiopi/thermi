# 0015. One stalled device never holds back the others' readings

**Status**: Accepted

## Context

The service reads every device in one pass each second (`LhmSensorSource`), and only sends a snapshot once the pass ends. In QA round 4, under Find it's load, the service's snapshots stopped for 3 to 5.5 s several times while a plain sampler at normal priority kept ticking: the stall was the service's own, not the PC's. The strong suspect is another LibreHardwareMonitor-based program, such as FanControl, writing fan speeds often under load and holding the Super I/O chip's bus mutex (`Global\Access_ISABUS.HTP.Method`), which every such program shares. The board's update then waits for the mutex, and with it every other device's reading, so the overlay goes blank and Find it loses the seconds it needs.

## Decision

- Each device, a board's Super I/O chip as one of its own, is updated in turn, as before, but off the read's thread, and the pass waits for it at most 500 ms. A device still updating then is left to finish, and the pass goes on with the next: one stalled device holds back none after it. The updates run on a thread of the service's own, never the thread pool's: a stalled one keeps its thread, which ends once the update returns, and the next goes to a new thread, so a stall as long as the read limit ties up one thread of its own and never starves the pool the pipe runs on.
- A device still updating is not started again until its update returns. A device is late from when an update of it came due and did not return within 500 ms, or failed, until one returns well. Meanwhile its sensors keep their last values while it is late by no more than the freshness limit the overlay applies to a whole snapshot (3 s, `LatestSnapshot.MaxAge`), and after that are sent as missing (null), so the catalog never changes for a stall and no frozen value looks live. A drive, updated every 5 s, keeps its values between its updates, and is late from the update that stalled or failed, so it keeps them 3 s as any device does.
- An update that fails, as a busy driver may answer nothing, is held the same way: the device keeps its last values for up to 3 s from when that update came due, then sends them as missing, and is updated again when next due. Only a device that never read is left out of the snapshot.
- A one-off `--dump` waits for every device, however long, since it is all the dump reads; a series keeps the service's 500 ms, as it keeps a snapshot a second.
- An update that takes longer than 500 ms, or fails, is written to the Application event log once it returns, with the device, how long it took and its failure, at most once a minute per device, so a busy bus shows without flooding the log. In a terminal, as with `--console` or `--dump`, it is written there instead.
- While an update is still running, the library is neither scanned again for new devices nor closed, even when a stop comes during the pass: either would pull the devices from under it.
- A device whose update has not returned within the read limit (60 s, `SensorPipeServer.ReadLimit`) ends the process, as a whole read past it did: only a process that ends frees a thread stuck in a driver, and Windows' recovery restarts the service.

## Rejected

- **Every device on a thread of its own, all at once**: the library updates its devices one after another, and several share drivers and buses; updating them all in parallel would trade one stall for races the library was never built for. Here devices overlap only with one that is stalled, which mostly waits on a mutex.
- **Sending each sensor's age on the pipe**: the client already treats a whole snapshot past 3 s as gone; a per-sensor age would change the wire (ADR 0012) for what nulls already say.

## Consequences

- With the bus held by another program, the CPU, GPU, memory and drives keep coming each second, and only the board's sensors go missing after 3 s.
- A read becomes asynchronous (`ISensorSource.ReadAsync`), and the pipe's server awaits it.
- What to watch: the event log's slow-update lines, to tell a busy bus from a driver that hangs.
- A device that fails its update keeps its sensors, as values or as missing, as one that is only slow does; in QA round 5 the CPU's update failed under Find it's load, between stalls of about a second, and leaving it out blanked its items for a second at a time, 5 s apart, and changed the catalog twice. Only one that never read is left out.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md`: "It reads sensors only while a client is connected, once per second" keeps its words, but a device stalled past 500 ms in a read is left to finish while the others are read and sent.
