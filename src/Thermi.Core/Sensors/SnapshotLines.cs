using System.Text;
using Thermi.Core.Tray;

namespace Thermi.Core.Sensors;

/// <summary>
/// Turns the bytes read from the pipe into snapshots. Bytes arrive in arbitrary pieces: a line, or a single
/// character, can span two reads, and one read can hold several lines.
/// </summary>
public sealed class SnapshotLines
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _pending = new();

    /// <summary>
    /// Returns the snapshots this piece completes. Throws <see cref="InvalidDataException"/> once a line
    /// outgrows any real snapshot: whatever is on the other end is not a sensor host.
    /// </summary>
    public IReadOnlyList<Snapshot> Add(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        _pending.Append(chars, 0, _decoder.GetChars(bytes, chars, flush: false));

        var snapshots = new List<Snapshot>();
        var text = _pending.ToString();
        var end = text.LastIndexOf('\n');
        if (end >= 0)
        {
            foreach (var line in text[..end].Split('\n'))
            {
                if (SnapshotJson.Read(line) is { } snapshot)
                {
                    snapshots.Add(snapshot);
                }
            }

            _pending.Clear().Append(text, end + 1, text.Length - end - 1);
        }

        if (_pending.Length > SnapshotJson.MaxLineLength)
        {
            throw new InvalidDataException("A line from the sensor pipe is longer than any real snapshot.");
        }

        return snapshots;
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
        Volatile.Read(ref _entry) is { } entry && time.GetUtcNow() - entry.At <= MaxAge ? entry.Snapshot : null;

    /// <summary>Running while snapshots are fresh, or awaited for less than <see cref="MaxAge"/>; else stopped or stale.</summary>
    public ServiceState State =>
        Current is not null ? ServiceState.Running
        : _lost ? ServiceState.Stopped
        : time.GetElapsedTime(Interlocked.Read(ref _heard)) > MaxAge ? ServiceState.Stale
        : ServiceState.Running;

    public void Set(Snapshot snapshot)
    {
        Volatile.Write(ref _entry, new Entry(snapshot, time.GetUtcNow()));
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

    private sealed record Entry(Snapshot Snapshot, DateTimeOffset At);
}
