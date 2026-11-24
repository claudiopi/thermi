using Thermi.Core.Pipe;
using Thermi.Core.Tray;

namespace Thermi.Core.Sensors;

/// <summary>
/// Turns the bytes read from the pipe into snapshots: a whole snapshot, names and all, then lines of its values alone,
/// each filled in with the names of the whole one before it. Values before any whole snapshot, or not as many as it has
/// sensors, are no snapshot. A sensor whose value did not change is the same object as in the snapshot before.
/// </summary>
public sealed class SnapshotLines
{
    private readonly LineBuffer _lines = new(SnapshotJson.MaxLineLength);
    private Snapshot? _last;

    /// <summary>
    /// Returns the snapshots this piece completes. Throws <see cref="InvalidDataException"/> once a line
    /// outgrows any real snapshot: whatever is on the other end is not a sensor host.
    /// </summary>
    public IReadOnlyList<Snapshot> Add(ReadOnlySpan<byte> bytes)
    {
        var snapshots = new List<Snapshot>();
        _lines.Add(bytes, line =>
        {
            if (Read(line) is { } snapshot)
            {
                _last = snapshot;
                snapshots.Add(snapshot);
            }
        });
        return snapshots;
    }

    private Snapshot? Read(ReadOnlySpan<byte> line)
    {
        var start = line.IndexOfAnyExcept(" \t\r"u8);
        if (start < 0 || line[start] != (byte)'[')
        {
            return SnapshotJson.Read(line);
        }

        if (_last is not { } last || SnapshotJson.ReadValues(line) is not { } values || values.Length != last.Sensors.Count)
        {
            return null;
        }

        var sensors = new RawSensor[values.Length];
        for (var at = 0; at < values.Length; at++)
        {
            var before = last.Sensors[at];
            sensors[at] = Nullable.Equals(before.Value, values[at]) ? before : before with { Value = values[at] };
        }

        return new Snapshot(sensors) { VirtualAdapters = last.VirtualAdapters };
    }
}

/// <summary>
/// The last snapshot, for as long as it is fresh. A host that stays connected but stops sending must not
/// leave old numbers on screen looking live: past <see cref="MaxAge"/> the snapshot counts as missing. It also tells why
/// none is fresh: the service is not running once the pipe could not be reached, and its data is stale once nothing came
/// for longer than that over a pipe that is still there, or since the reader started.
/// </summary>
public sealed class LatestSnapshot(TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);

    private Entry? _entry;

    // When the pipe was last heard from: a snapshot, a connection, or the reader's start.
    private long _heard = time.GetTimestamp();
    private volatile bool _lost;

    public Snapshot? Current =>
        Volatile.Read(ref _entry) is { } entry && time.GetElapsedTime(entry.At) <= MaxAge ? entry.Snapshot : null;

    /// <summary>Running while snapshots are fresh, or awaited for less than <see cref="MaxAge"/>; else stopped or stale.</summary>
    public ServiceState State =>
        Current is not null ? ServiceState.Running
        : _lost ? ServiceState.Stopped
        : time.GetElapsedTime(Interlocked.Read(ref _heard)) > MaxAge ? ServiceState.Stale
        : ServiceState.Running;

    public void Set(Snapshot snapshot)
    {
        Volatile.Write(ref _entry, new Entry(snapshot, time.GetTimestamp()));
        Connected();
    }

    /// <summary>The pipe was reached and its owner trusted; snapshots are awaited from now.</summary>
    public void Connected()
    {
        Interlocked.Exchange(ref _heard, time.GetTimestamp());
        _lost = false;
    }

    /// <summary>The pipe could not be reached, or ended: no snapshot is fresh, and the service counts as stopped until it is reached again.</summary>
    public void Lost()
    {
        Volatile.Write(ref _entry, null);
        _lost = true;
    }

    // On the counter, never the wall clock: one set back would keep frozen numbers looking live.
    private sealed record Entry(Snapshot Snapshot, long At);
}
