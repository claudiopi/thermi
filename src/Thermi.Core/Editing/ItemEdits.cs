using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// The window's edits of one item, found by its id. Each gives valid settings, or the settings it was given
/// and the reason it could not change them. The snapshot is what the service reads now, which gives each
/// sensor its type and name.
/// </summary>
public static class ItemEdits
{
    /// <summary>
    /// Adds a sensor as an item, after the last item of its device's pill, or at the end when the overlay has none, named
    /// in the language of <paramref name="texts"/>, English when there are none.
    /// </summary>
    public static EditResult AddSensor(ThermiConfig config, int overlay, Snapshot snapshot, string sensorId, Texts? texts = null) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
        {
            if (SensorMap.Find(snapshot, sensorId) is not { } sensor || SensorMap.KindOf(sensor) is null)
            {
                return ConfigEdits.Refuse(config, Refusal.SensorNotFound);
            }

            // A battery's rate is a few watts, so whole watts would hide most of its change.
            var language = texts ?? Texts.English;
            var decimals = SensorMap.MetricOf(sensor) == Metrics.BatteryRate ? 1 : (int?)null;
            var name = Names.OfItem(config, sensor, sensorId, language);
            var read = OtherGpu(config.Overlays[overlay], snapshot, sensor);
            var chip = read is not null && SensorMap.GpuAfter(sensor, read);

            var item = new ItemConfig
            {
                Sensor = sensorId,
                Name = read is null ? name : language.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, name, sensor.Hardware),
                Label = chip ? language.Of(NameText.OtherGpu) : null,
                Decimals = decimals,
                Own = read is not null && !chip,
            };

            // It starts as the defaults of its kind do: warned by its own reading, with its kind's calm lines.
            return Insert(config, overlay, sensor, item with { Threshold = Threshold.Default(item, snapshot) }, language);
        });

    // A PC with a graphics chip in its CPU and a graphics card has two GPUs, and the GPU pill reads one, returned here: a
    // sensor of the other is named by its GPU and goes to a pill of its own, never reading as the pill's. The chip's pill
    // is the iGPU one, which its later sensors join; a card's, beside a GPU pill set to read the chip, starts a GPU pill.
    private static RawSensor? OtherGpu(OverlayConfig overlay, Snapshot snapshot, RawSensor sensor) =>
        SensorMap.IsGpu(sensor)
        && SensorMap.Chosen(snapshot, overlay).GetValueOrDefault(Metrics.GpuLoad.Id) is { } read
        && read.Hardware != sensor.Hardware ? read : null;

    /// <summary>Puts a new item in its device's pill, or in the pill of the label it comes with, with that pill's label.</summary>
    internal static EditResult Insert(ThermiConfig config, int overlay, RawSensor sensor, ItemConfig item, Texts texts)
    {
        var items = config.Overlays[overlay].Items;
        var (at, label) = Names.PlaceOf(config.Overlays[overlay], sensor, texts, item.Label);
        return ConfigEdits.Apply(
            config,
            current => ConfigEdits.WithItems(current, overlay, [.. items.Take(at), item with { Label = label }, .. items.Skip(at)]));
    }

    /// <summary>Gives the item a name of its own, as the window calls it; the label its pill shows stays.</summary>
    public static EditResult Rename(ThermiConfig config, string id, string name) =>
        name.Trim() is { Length: > 0 } trimmed ? ConfigEdits.Edit(config, id, item => item with { Name = trimmed })
        : ConfigEdits.Find(config, id) is null ? ConfigEdits.Refuse(config, Refusal.NotFound)
        : ConfigEdits.Refuse(config, Refusal.NameEmpty);

    public static EditResult Delete(ThermiConfig config, string id)
    {
        if (ConfigEdits.Find(config, id) is not var (overlay, index))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var items = config.Overlays[overlay].Items;
        return ConfigEdits.Apply(config, current => ConfigEdits.WithItems(current, overlay, [.. items.Where((_, at) => at != index)]));
    }

    /// <summary>Moves an item to <paramref name="index"/> in its overlay, counted without it; past either end is that end.</summary>
    public static EditResult Move(ThermiConfig config, string id, int index)
    {
        if (ConfigEdits.Find(config, id) is not var (overlay, from))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var items = config.Overlays[overlay].Items.ToList();
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(Math.Clamp(index, 0, items.Count), item);
        return ConfigEdits.Apply(config, current => ConfigEdits.WithItems(current, overlay, items));
    }

    /// <summary>
    /// Shows another sensor of the same type. An item made from a metric keeps it as auto, an item warned by its own
    /// reading is warned by the new sensor, and a fan item warned by a fan's duty is warned by the new fan's own duty.
    /// </summary>
    public static EditResult ChangeSensor(ThermiConfig config, string id, Snapshot snapshot, string sensorId)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (SensorMap.Find(snapshot, sensorId) is not { } sensor || SensorMap.KindOf(sensor) is not { } kind)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorNotFound);
        }

        if (KindOf(item, snapshot) is not { } known)
        {
            return ConfigEdits.Refuse(config, Refusal.TypeUnknown);
        }

        if (known != kind)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorOfAnotherType);
        }

        var warnedByItsDuty = kind == SensorKind.Fan && WarnedByItsDuty(item);
        return ConfigEdits.Edit(config, id, each =>
        {
            var named = Named(each, sensorId);
            return warnedByItsDuty ? WithDutyOf(named, sensorId, each.WarnBy?.Metric ?? each.WarnBy?.Auto) : named;
        });
    }

    /// <summary>
    /// The fan finder's pick: the first item of the overlay that shows the CPU fan names that fan, as
    /// <see cref="ChangeSensor"/> does, so it keeps what it warns by. No item is added: the finder runs from that item's page.
    /// </summary>
    public static EditResult UseCpuFan(ThermiConfig config, int overlay, Snapshot snapshot, string fanId) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            config.Overlays[overlay].Items.FirstOrDefault(item => (item.Metric ?? item.Auto) == Metrics.CpuFan.Id) is { } fan
                ? ChangeSensor(config, fan.Id, snapshot, fanId)
                : ConfigEdits.Refuse(config, Refusal.NotFound));

    /// <summary>Goes back to the metric the item started from, and what it warns by to its own metric too.</summary>
    public static EditResult BackToAuto(ThermiConfig config, string id)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (item.Metric is null && item.Auto is null)
        {
            return ConfigEdits.Refuse(config, Refusal.NoAuto);
        }

        return ConfigEdits.Edit(config, id, each => each with
        {
            Metric = each.Metric ?? each.Auto,
            Sensor = null,
            Sensors = null,
            Combine = null,
            Auto = null,
            WarnBy = each.WarnBy is { Auto: { } warnBy } ? new WarnByConfig { Metric = warnBy } : each.WarnBy,
        });
    }

    /// <summary>
    /// Warns by another reading of the item, or with null by its own, the item's own named as one being its own; what it
    /// warns by then brings its default lines. A reading to warn by is a temperature, a load, a duty, a fan's speed or a
    /// throughput, and an item lines can then judge no more, as the RAM in use by itself, loses its lines, alert-only, the
    /// notice and its graph.
    /// </summary>
    public static EditResult SetWarnBy(ThermiConfig config, string id, Snapshot snapshot, WarnByConfig? warnBy)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var other = warnBy is not null && !Threshold.IsOwn(item, warnBy) ? warnBy : null;
        Refusal? refusal = other is null ? null
            : KindOf(other.Metric ?? other.Auto, other.Sensor, snapshot) is not { } kind ? Refusal.SensorNotFound
            : !Threshold.CanMeter(kind) ? Refusal.CannotWarnBy
            : null;
        return refusal is { } refused
            ? ConfigEdits.Refuse(config, refused)
            : ConfigEdits.Edit(config, id, each => each.WarnBy == other ? each : Rejudged(each with { WarnBy = other }, snapshot));
    }

    /// <summary>
    /// Sets the lines at which the item warns and is critical, in the unit of what it warns by: from 0 to 100, or to 10,000
    /// RPM for a fan's speed. A line left null is off, and with both off the item has none.
    /// </summary>
    public static EditResult SetThreshold(ThermiConfig config, string id, double? warning, double? critical)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var kind = Threshold.JudgedKind(item);
        var max = kind is { } judged ? Threshold.MaxOf(judged) : 0;
        Refusal? refusal = kind is null ? Refusal.NothingToJudge
            : !OnTrack(warning, max) || !OnTrack(critical, max) ? Refusal.ThresholdOutOfRange
            : warning > critical ? Refusal.WarningAboveCritical
            : null;
        return refusal is { } refused
            ? ConfigEdits.Refuse(config, refused)
            : ConfigEdits.Edit(config, id, each => each with { Threshold = warning is null && critical is null ? null : new Threshold(warning, critical) });
    }

    /// <summary>Gives the item back its default lines, which follow what it warns by, or none.</summary>
    public static EditResult ResetThreshold(ThermiConfig config, string id, Snapshot snapshot)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (!Threshold.Judges(item))
        {
            return ConfigEdits.Refuse(config, Refusal.NothingToJudge);
        }

        var reading = item.WarnBy ?? Threshold.Own(item);
        return KindOf(reading.Metric ?? reading.Auto, reading.Sensor, snapshot) is null
            ? ConfigEdits.Refuse(config, Refusal.SensorNotFound)
            : ConfigEdits.Edit(config, id, each => each with { Threshold = Threshold.Default(each, snapshot) });
    }

    private static bool OnTrack(double? line, double max) => line is not { } value || (value >= 0 && value <= max);

    private static ItemConfig Rejudged(ItemConfig item, Snapshot snapshot) =>
        Threshold.Judges(item)
            ? item with { Threshold = Threshold.Default(item, snapshot) }
            : item with { Threshold = null, AlertOnly = false, Notify = false, Graph = null };

    /// <summary>Makes the item start a pill of its own, with the items after it that share its label, or join the run before it again.</summary>
    public static EditResult SetOwn(ThermiConfig config, string id, bool own) => ConfigEdits.Edit(config, id, item => item with { Own = own });

    public static EditResult SetTray(ThermiConfig config, string id, bool tray) => ConfigEdits.Edit(config, id, item => item with { Tray = tray });

    /// <summary>Gives the item's value a color of its own as #RRGGBB over the style's, or with null the style's again.</summary>
    public static EditResult SetColor(ThermiConfig config, string id, string? color) => ConfigEdits.Edit(config, id, item => item with { Color = color });

    /// <summary>
    /// Gives every item of the item's pill the label, and no other item: the run of its label with pills or a single bar,
    /// the item alone with each. An item after them that showed their label without one of its own keeps showing it.
    /// </summary>
    public static EditResult RenamePill(ThermiConfig config, string id, string label)
    {
        if (ConfigEdits.Find(config, id) is not var (overlay, index))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var trimmed = label.Trim();
        if (trimmed.Length == 0)
        {
            return ConfigEdits.Refuse(config, Refusal.LabelEmpty);
        }

        var items = config.Overlays[overlay].Items;
        var labels = OverlayLayout.RunLabels(items);
        var (first, last) = PillOf(config.Overlays[overlay], index);
        var followers = last + 1;
        while (followers < items.Count && OverlayLayout.LabelOf(items[followers]) is null)
        {
            followers++;
        }

        return ConfigEdits.Apply(config, current => ConfigEdits.WithItems(current, overlay, [.. items.Select((item, at) =>
            at >= first && at <= last ? item with { Label = trimmed }
            : at > last && at < followers ? item with { Label = labels[at] }
            : item)]));
    }

    /// <summary>The first and last item of the pill the item at <paramref name="index"/> is in, those a rename of its label changes.</summary>
    public static (int First, int Last) PillOf(OverlayConfig overlay, int index)
    {
        var run = OverlayLayout.Runs(overlay).First(each => each.First <= index && index <= each.Last);
        return (run.First, run.Last);
    }

    private static bool WarnedByItsDuty(ItemConfig item) => item.WarnBy is { } warnBy
        && (warnBy.Metric == Metrics.CpuFanDuty.Id
            || warnBy.Auto == Metrics.CpuFanDuty.Id
            || (item.Sensor is { } fan && warnBy.Sensor == SensorMap.OwnDuty(fan)));

    // An item made from a metric keeps it as auto, so it can go back to it.
    private static ItemConfig Named(ItemConfig item, string sensor) =>
        item with { Metric = null, Sensor = sensor, Sensors = null, Combine = null, Auto = item.Metric ?? item.Auto };

    // A duty the board does not report is still named, so it shows as missing rather than another fan's duty; a fan with
    // no duty at all is warned by its own speed, whose lines are not the duty's.
    private static ItemConfig WithDutyOf(ItemConfig item, string fan, string? auto) => SensorMap.OwnDuty(fan) is { } duty
        ? item with { WarnBy = new WarnByConfig { Sensor = duty, Auto = auto } }
        : item with { WarnBy = null, Threshold = null };

    /// <summary>The type of what an item shows, or null when none of its sensors is among those the service reads.</summary>
    public static SensorKind? KindOf(ItemConfig item, Snapshot snapshot) =>
        Metrics.Find(item.Metric ?? item.Auto)?.Kind
        ?? SensorsOf(item).Select(sensor => KindOf(metric: null, sensor, snapshot)).FirstOrDefault(kind => kind is not null);

    private static IReadOnlyList<string> SensorsOf(ItemConfig item) => item.Sensors ?? (item.Sensor is { } sensor ? [sensor] : []);

    // A metric says its type even when its sensor is gone; a sensor says it only while the service reads it.
    private static SensorKind? KindOf(string? metric, string? sensor, Snapshot snapshot) =>
        Metrics.Find(metric)?.Kind ?? (sensor is not null && SensorMap.Find(snapshot, sensor) is { } read ? SensorMap.KindOf(read) : null);
}
