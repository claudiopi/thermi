using System.Text;

namespace Thermi.Core;

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
/// leave old numbers on screen looking live: past <see cref="MaxAge"/> the snapshot counts as missing.
/// </summary>
public sealed class LatestSnapshot(TimeProvider time)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);

    private Entry? _entry;

    public Snapshot? Current =>
        Volatile.Read(ref _entry) is { } entry && time.GetUtcNow() - entry.At <= MaxAge ? entry.Snapshot : null;

    public void Set(Snapshot snapshot) => Volatile.Write(ref _entry, new Entry(snapshot, time.GetUtcNow()));

    public void Clear() => Volatile.Write(ref _entry, null);

    private sealed record Entry(Snapshot Snapshot, DateTimeOffset At);
}
