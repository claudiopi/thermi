# 0003. The sensor host is a Windows service

**Status**: Accepted, amended by `docs/adr/0016-admin-on-demand.md`

## Context

The sensor host needs admin rights (`docs/adr/0001-architecture.md`), must be running before the overlay wants data, and must come back on its own if it crashes. Asking for admin rights at every sign-in is not acceptable.

## Decision

- The sensor host runs as a Windows service named `thermi-sensors`, as LocalSystem, starting automatically. Windows restarts it if it fails.
- Of LocalSystem's privileges it keeps only `SeChangeNotifyPrivilege`, through the service's required privileges, which `scripts/install.ps1` sets. Measured on a Ryzen 9 9950X with an AMD and an NVIDIA GPU: a dump as an administrator, as LocalSystem with every privilege and as LocalSystem with that one alone read the same 249 sensors, every CPU temperature, power and clock included. LibreHardwareMonitor and its driver need the Administrators group, which LocalSystem keeps, not privileges. The service takes no service SID of its own: nothing is granted to it, and the pipe it creates stays owned by SYSTEM, the owner the overlay trusts.
- Its files live under Program Files, where only admins can write. A service binary in a folder the user can write to would let any program replace it and run as SYSTEM.
- It reads sensors only while a client is connected, once per second, and writes one JSON line per reading to the named pipe `thermi-sensors`. It never reads from the pipe, so there is no client input to parse.
- The pipe allows local interactive users to read, administrators and SYSTEM full control, and denies network access. The host only creates it if no one else has; the overlay only trusts a pipe owned by SYSTEM or administrators. That stops another user from posing as the host while it restarts. A host that finds the name taken waits and tries again every 5 s instead of failing, since a failure would restart it into the same taken name every minute.
- Any interactive user may open the pipe, so a Windows session holds at most 4 of its 32 connections, and a client past its session's share is hung up at once: one user holding the pipe open never locks out another's overlay. A client that stops reading is dropped after 2 s.
- If the host fails, it stops with an error, and Windows restarts it. It never stays running but silent; the overlay also treats a snapshot older than 3 seconds as missing.
- For development it also runs in a terminal (`--console`, as administrator, or the overlay will not trust it), and `--dump <file>` saves one snapshot.
- The overlay is a normal program in the user's session (a service cannot show windows), started at sign-in.

## What it replaces in earlier ADRs

- `docs/adr/0001-architecture.md` says only the signed-in user can open the pipe and that the host accepts only "send readings". Instead, local users may only read the pipe, as above, and the host never reads from it, so there is nothing to send it.
- `docs/adr/0002-sensors.md` plans an `ISensorSource` interface and a replay source. Instead, everything after the one class that talks to LibreHardwareMonitor works on snapshots, and snapshots recorded with `thermi-sensors --dump` (`tests/Thermi.Tests/Recordings`) replay through the same path as live data. A dump holds hardware model names and readings only. The no-admin and no-driver paths are tested through recordings made without them.

## Rejected

- **A scheduled task that runs the host elevated at sign-in**: no restart on failure, and it starts only after sign-in.
- **A service that the overlay sends commands to**: more attack surface for no need today.

## Consequences

- Installing or updating the service needs admin rights once; the overlay never does.
- Sending every sensor each second is the simplest protocol. If it shows up in the budget, the host sends sensor names once and only values afterwards.
