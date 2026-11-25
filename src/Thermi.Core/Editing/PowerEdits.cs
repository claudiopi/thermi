using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// The window's edits of an item's power options: combined sensors, the offset, its minimum, maximum or average, smoothing,
/// decimals, the graph and alert-only. Each gives valid settings, or the settings it was given and why it could not.
/// The snapshot gives each sensor its type, which the offset's range and the sum's refusal follow.
/// </summary>
public static class PowerEdits
{
    /// <summary>The widest offset any type takes, which a file is held to where only the service can say the item's type.</summary>
    public static (double Min, double Max) WidestOffset { get; } = (-10_000, 10_000);

    /// <summary>The offset a type takes, in its unit; a temperature's is a difference, kept in °C.</summary>
    public static (double Min, double Max) OffsetRange(SensorKind kind) => kind switch
    {
        SensorKind.Temperature or SensorKind.Load or SensorKind.Control => (-20, 20),
        SensorKind.Fan => (-2000, 2000),
        SensorKind.Power => (-500, 500),
        SensorKind.Clock => WidestOffset,
        SensorKind.Data => (-100, 100),
        // A speed or a time remaining is counted, not measured, so there is no sensor's error to correct.
        SensorKind.Throughput or SensorKind.Duration => (0, 0),

        // What Windows says is no measure at all, and frames are counted.
        SensorKind.Time or SensorKind.Address or SensorKind.FrameRate or SensorKind.FrameTime => (0, 0),

        // A board's divider errs by a factor, not by a sum, so an offset corrects no voltage.
        SensorKind.Voltage => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sensor kind."),
    };

    /// <summary>
    /// Adds an item that combines sensors of one type, as their highest, average or sum, placed and named from the first
    /// sensor as a sensor's item is. A sum of temperatures means nothing, so it is refused.
    /// </summary>
    public static EditResult AddCombined(
        ThermiConfig config,
        int overlay,
        Snapshot snapshot,
        IReadOnlyList<string> sensorIds,
        Combine combine,
        Texts? texts = null) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
        {
            var ids = sensorIds.Distinct(StringComparer.Ordinal).ToList();
            var sensors = ids.Select(id => SensorMap.Find(snapshot, id)).ToList();
            if (Refused(ids.Count, [.. sensors.Select(sensor => sensor is null ? null : SensorMap.KindOf(sensor)).Distinct()], combine) is { } refused)
            {
                return ConfigEdits.Refuse(config, refused);
            }

            var language = texts ?? Texts.English;
            var first = sensors[0]!;
            var item = new ItemConfig { Sensors = ids, Combine = combine };
            return ItemEdits.Insert(config, overlay, first, item, language);
        });

    /// <summary>
    /// Makes an item show sensors of one type combined, as their highest unless it already combined others; it keeps the
    /// metric it started from as auto, so it can go back to it. A sum of temperatures means nothing, so it is refused.
    /// </summary>
    public static EditResult SetSensors(ThermiConfig config, string id, Snapshot snapshot, IReadOnlyList<string> sensorIds)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var ids = sensorIds.Distinct(StringComparer.Ordinal).ToList();
        var combine = item.Combine ?? Combine.Max;
        var kinds = ids.Select(sensor => SensorMap.Find(snapshot, sensor) is { } read ? SensorMap.KindOf(read) : null).Distinct().ToList();
        return Refused(ids.Count, kinds, combine) is { } refused
            ? ConfigEdits.Refuse(config, refused)
            : ConfigEdits.Edit(
                config,
                id,
                each => each with { Metric = null, Sensor = null, Sensors = ids, Combine = combine, Auto = each.Metric ?? each.Auto });
    }

    /// <summary>Changes how a combined item joins its sensors; never into a sum of temperatures, nor of sensors whose type is not known now.</summary>
    public static EditResult SetCombine(ThermiConfig config, string id, Snapshot snapshot, Combine combine)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var kind = ItemEdits.KindOf(item, snapshot);
        Refusal? refusal = item.Sensors is null ? Refusal.CombineNeedsTwo
            : combine != Combine.Sum ? null
            : kind is null ? Refusal.SumOfUnknown
            : kind == SensorKind.Temperature ? Refusal.SumOfTemperatures
            : null;
        return refusal is { } refused ? ConfigEdits.Refuse(config, refused) : ConfigEdits.Edit(config, id, each => each with { Combine = combine });
    }

    /// <summary>
    /// Sets the fixed offset added to the item's value, within its type's range;
    /// a temperature's in °C, as <see cref="Temperatures.KeptDifference"/> gives it.
    /// </summary>
    public static EditResult SetOffset(ThermiConfig config, string id, Snapshot snapshot, double offset)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (ItemEdits.KindOf(item, snapshot) is not { } kind)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorNotFound);
        }

        var (min, max) = OffsetRange(kind);
        return offset >= min && offset <= max
            ? ConfigEdits.Edit(config, id, each => each with { Offset = offset })
            : ConfigEdits.Refuse(config, Refusal.OffsetOutOfRange);
    }

    /// <summary>Shows the value now, or its minimum, maximum or average since the overlay started or was reset.</summary>
    public static EditResult SetStat(ThermiConfig config, string id, ItemStat stat) => ConfigEdits.Edit(config, id, item => item with { Stat = stat });

    /// <summary>Averages the value over its last 2 to 10 seconds, or with 0 shows each one as it comes.</summary>
    public static EditResult SetSmooth(ThermiConfig config, string id, int seconds) => ConfigEdits.Edit(config, id, item => item with { Smooth = seconds });

    /// <summary>Shows 0, 1 or 2 decimals, or with null its type's own; the room the item keeps follows.</summary>
    public static EditResult SetDecimals(ThermiConfig config, string id, int? decimals) =>
        ConfigEdits.Edit(config, id, item => item with { Decimals = decimals });

    /// <summary>Shows a throughput in bits per second, or back in bytes; its lines and the file stay in bytes.</summary>
    public static EditResult SetBits(ThermiConfig config, string id, bool bits) => ConfigEdits.Edit(config, id, item => item with { Bits = bits });

    /// <summary>
    /// Draws nothing, bars or a line of the last minute beside the value, or with null what its overlay draws; only an item
    /// lines can judge has a level and a scale to draw.
    /// </summary>
    public static EditResult SetGraph(ThermiConfig config, string id, GraphStyle? graph) =>
        Judged(config, id, graph is not null, item => item with { Graph = graph });

    /// <summary>Shows the item only while its lines say warning or critical; an item lines cannot judge never would.</summary>
    public static EditResult SetAlertOnly(ThermiConfig config, string id, bool alertOnly) =>
        Judged(config, id, alertOnly, item => item with { AlertOnly = alertOnly });

    /// <summary>Notifies once the item has been critical a while; only an item lines can judge can be.</summary>
    public static EditResult SetNotify(ThermiConfig config, string id, bool notify) => Judged(config, id, notify, item => item with { Notify = notify });

    private static Refusal? Refused(int count, List<SensorKind?> kinds, Combine combine) =>
        count < 2 ? Refusal.CombineNeedsTwo
        : kinds.Contains(null) ? Refusal.SensorNotFound
        : kinds.Count > 1 ? Refusal.SensorOfAnotherType
        : kinds[0] == SensorKind.Temperature && combine == Combine.Sum ? Refusal.SumOfTemperatures
        : null;

    private static EditResult Judged(ThermiConfig config, string id, bool on, Func<ItemConfig, ItemConfig> change) =>
        on && ConfigEdits.Item(config, id) is { } item && !Threshold.Judges(item)
            ? ConfigEdits.Refuse(config, Refusal.JudgingNeeded)
            : ConfigEdits.Edit(config, id, change);
}
