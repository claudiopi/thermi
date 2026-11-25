using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Ui;

/// <summary>
/// What the window has read since it opened: ten minutes of every sensor the catalog lists and every metric of the overlay
/// shown, under the ids items and meters name them by, in Core's fixed rings. Every sensor is kept, not only those chosen,
/// so a sensor or meter chosen later shows its sparkline and chart from the window's opening.
/// </summary>
internal sealed class Readings(TimeProvider time)
{
    private static readonly Snapshot Nothing = new([]);

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    // Each overlay's readings and picks of the last snapshot, by the overlay as the settings hold it, which the history,
    // the charts and the view all read each second.
    private readonly Dictionary<OverlayConfig, IReadOnlyDictionary<string, Reading>> _reads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<OverlayConfig, IReadOnlyDictionary<string, RawSensor?>> _picks = new(ReferenceEqualityComparer.Instance);
    private Snapshot? _readOf;
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

    /// <summary>What an overlay reads of a snapshot, as <see cref="SensorMap.Read(Snapshot, OverlayConfig, string?)"/>, read once per snapshot.</summary>
    public IReadOnlyDictionary<string, Reading> ReadOf(Snapshot snapshot, OverlayConfig overlay) =>
        Kept(_reads, snapshot, overlay, () => SensorMap.Read(snapshot, overlay));

    /// <summary>The sensor each metric of an overlay reads, as <see cref="SensorMap.Chosen(Snapshot, OverlayConfig, string?)"/>, once per snapshot.</summary>
    public IReadOnlyDictionary<string, RawSensor?> ChosenOf(Snapshot snapshot, OverlayConfig overlay) =>
        Kept(_picks, snapshot, overlay, () => SensorMap.Chosen(snapshot, overlay));

    /// <summary>What names sensors and their types: the snapshot now, or while none comes the last fresh one, or none at all.</summary>
    public Snapshot Known(Snapshot? now) => now ?? Last ?? Nothing;

    /// <summary>Keeps a fresh snapshot: every sensor of the catalog, and every metric as the overlay shown picks it.</summary>
    public void Record(Snapshot snapshot, OverlayConfig overlay)
    {
        Last = snapshot;
        var readings = new Dictionary<string, Reading>(ReadOf(snapshot, overlay), StringComparer.Ordinal);
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

    private T Kept<T>(Dictionary<OverlayConfig, T> kept, Snapshot snapshot, OverlayConfig overlay, Func<T> read)
    {
        if (!ReferenceEquals(snapshot, _readOf))
        {
            _readOf = snapshot;
            _reads.Clear();
            _picks.Clear();
        }

        if (!kept.TryGetValue(overlay, out var value))
        {
            kept[overlay] = value = read();
        }

        return value;
    }
}
