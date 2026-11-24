using System.Buffers;
using Thermi.Core.Sensors;

namespace Thermi.Core.Pipe;

/// <summary>
/// The sensor service's side of the pipe: each second's snapshot as the line each client needs, the whole snapshot for a
/// client that has not had this catalog, its sensors' ids, names, types and devices and the virtual adapters, and its
/// values alone for one that has. Each line is written once a second at most, only when some client needs it, straight as
/// UTF-8 into a buffer kept from one second to the next.
/// </summary>
public sealed class SnapshotWriter
{
    // A buffer a client may still be sending from is left to it, and a new one of the same size taken.
    private const int FirstSize = 4096;

    private ArrayBufferWriter<byte> _whole = new(FirstSize);
    private ArrayBufferWriter<byte> _values = new(FirstSize);
    private Snapshot? _snapshot;

    /// <summary>The catalog of the last snapshot, counted from 1; a client that has had none is at 0.</summary>
    public int Catalog { get; private set; }

    /// <summary>
    /// Takes the snapshot of this second. With <paramref name="sending"/>, some client is still sending last second's
    /// line, so this second's go into new buffers.
    /// </summary>
    public void Next(Snapshot snapshot, bool sending)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_snapshot is not { } before || !SameCatalog(before, snapshot))
        {
            Catalog++;
        }

        _snapshot = snapshot;
        if (sending)
        {
            _whole = new ArrayBufferWriter<byte>(Math.Max(FirstSize, _whole.Capacity));
            _values = new ArrayBufferWriter<byte>(Math.Max(FirstSize, _values.Capacity));
        }
        else
        {
            _whole.ResetWrittenCount();
            _values.ResetWrittenCount();
        }
    }

    /// <summary>This second's line for a client that last had catalog <paramref name="had"/>, after which it has <see cref="Catalog"/>.</summary>
    public ReadOnlyMemory<byte> LineFor(int had)
    {
        var snapshot = _snapshot ?? throw new InvalidOperationException("No snapshot was taken yet.");
        var whole = had != Catalog;
        var line = whole ? _whole : _values;
        if (line.WrittenCount == 0)
        {
            if (whole)
            {
                SnapshotJson.WriteLine(snapshot, line);
            }
            else
            {
                SnapshotJson.WriteValues(snapshot, line);
            }
        }

        return line.WrittenMemory;
    }

    // The same sensors in the same order, by everything but their values.
    private static bool SameCatalog(Snapshot before, Snapshot now)
    {
        if (before.Sensors.Count != now.Sensors.Count || !(before.VirtualAdapters ?? []).SequenceEqual(now.VirtualAdapters ?? [], StringComparer.Ordinal))
        {
            return false;
        }

        for (var at = 0; at < now.Sensors.Count; at++)
        {
            var (was, @is) = (before.Sensors[at], now.Sensors[at]);
            if (was.Id != @is.Id || was.Name != @is.Name || was.Type != @is.Type || was.Hardware != @is.Hardware || was.HardwareType != @is.HardwareType)
            {
                return false;
            }
        }

        return true;
    }
}
