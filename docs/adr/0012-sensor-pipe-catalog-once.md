# 0012. The sensor pipe sends its catalog once, then values

**Status**: Accepted

## Context

ADR 0003 has the service write every sensor, names and all, as one JSON line a second. On the author's PC (Ryzen 9 9950X, RTX 5060 Ti, two drives, two adapters) that line is 38 KB for 249 sensors, of which the values are about 2 KB; the overlay decoded it into three copies a second, and rescanned all it held on each piece of a line that came in many. ADR 0003 foresaw it: "If it shows up in the budget, the host sends sensor names once and only values afterwards."

## Decision

- To each client, the service sends the whole snapshot, as a recording holds it (`SnapshotJson.WriteLine`), on connecting and on the second after any change of its sensors' ids, names, types or devices or of the virtual adapters. Every other second it sends the values alone, a JSON array in the order of that whole snapshot, `null`, `"NaN"` and `"Infinity"` as before (`SnapshotJson.WriteValues`).
- `src/Thermi.Core/Pipe/SnapshotWriter.cs` writes each line once a second at most, only when some client needs it, into buffers kept from one second to the next; each client remembers the catalog it was last sent.
- A client (`SnapshotLines`) cuts lines from bytes (`src/Thermi.Core/Pipe/LineBuffer.cs`), searching only the new bytes, parses them as UTF-8 with no string between, and fills each values line in with the names of the whole snapshot before it, keeping a sensor whose value did not change as the same object. Values without a whole snapshot before them, or not one per sensor, are no snapshot.
- The pipe stays one-way: the service still never reads from it, so there is still no client input to parse.

## Rejected

- **Values as binary**: a JSON array is already 2 KB a second and stays readable in a dump of the pipe.
- **A new format for the whole line**: keeping the recordings' format lets a new client read a service from before, and every recording replays as the first line of a connection.
- **Reading only what clients show**, with each client telling the service its devices: the service would have to parse what any local user writes, as SYSTEM, which ADR 0003 rules out.

## Consequences

- A client reads about 2 KB a second instead of 38 KB, and its cost of cutting lines no longer grows with how finely they arrive.
- A client of an older version reads the first line and then nothing it understands; the installer replaces both together.

## What it replaces in earlier ADRs

- `docs/adr/0003-sensor-service.md`: "writes one JSON line per reading to the named pipe `thermi-sensors`" becomes: it writes the whole snapshot to each client on connecting and on any change of its sensors, and the values alone every other second. Its consequence on sending names once is this decision.
