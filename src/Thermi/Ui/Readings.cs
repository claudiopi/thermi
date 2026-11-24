using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Ui;

/// <summary>
/// What the window has read since it opened: ten minutes of every sensor the catalog lists and every metric of the overlay
/// shown, under the ids items and meters name them by, in Core's fixed rings. Every sensor is kept, not only those chosen,
/// so a sensor or meter chosen later shows its sparkline and histogram from the window's opening.
/// </summary>
internal sealed class Readings(TimeProvider time)
{
    private static readonly Snapshot Nothing = new([]);

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private Snapshot? _built;
    private IReadOnlyList<CatalogDevice> _catalog = [];

    public SensorHistory History { get; } = new(time);

    /// <summary>The sensors seen lively since the window opened, by their catalog ids, which the catalog shows even when idle.</summary>
    public IReadOnlySet<string> Seen => _seen;

    /// <summary>The last fresh snapshot, which still names sensors and their types while no fresh one comes.</summary>
    public Snapshot? Last { get; private set; }

    /// <summary>When the window opened, which its history starts from.</summary>
    public DateTimeOffset Opened { get; } = time.GetUtcNow();

    /// <summary>The catalog of a snapshot, built once per snapshot; none without one, as while no fresh readings come.</summary>
    public IReadOnlyList<CatalogDevice> CatalogOf(Snapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        if (!ReferenceEquals(snapshot, _built))
        {
            (_built, _catalog) = (snapshot, SensorCatalog.Build(snapshot));
        }

        return _catalog;
    }

    /// <summary>What names sensors and their types: the snapshot now, or while none comes the last fresh one, or none at all.</summary>
    public Snapshot Known(Snapshot? now) => now ?? Last ?? Nothing;

    /// <summary>Keeps a fresh snapshot: every sensor of the catalog, and every metric as the overlay shown picks it.</summary>
    public void Record(Snapshot snapshot, OverlayConfig overlay)
    {
        Last = snapshot;
        var readings = new Dictionary<string, Reading>(SensorMap.Read(snapshot, overlay), StringComparer.Ordinal);
        foreach (var sensor in CatalogOf(snapshot).SelectMany(device => device.Sensors))
        {
            readings[sensor.Id] = sensor.Reading;
            if (SensorCatalog.Lively(sensor))
            {
                _seen.Add(sensor.Id);
            }
        }

        History.Add(readings);
    }
}
