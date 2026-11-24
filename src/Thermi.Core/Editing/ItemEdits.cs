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

            // It starts as the defaults of its kind do: metered by itself, with its kind's calm lines, where it can be a meter.
            var itself = Threshold.CanMeter(SensorMap.KindOf(sensor)!.Value) ? new MeterConfig { Sensor = sensorId } : null;
            var item = new ItemConfig
            {
                Sensor = sensorId,
                Name = read is null ? name : language.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, name, sensor.Hardware),
                Label = chip ? language.Of(NameText.OtherGpu) : null,
                Decimals = decimals,
                Own = read is not null && !chip,
                Meter = itself,
                Threshold = itself is null ? null : Threshold.Default(itself, snapshot),
            };
            return Insert(config, overlay, sensor, item, language);
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
    /// Shows another sensor of the same type. An item made from a metric keeps it as auto, an item metered by itself
    /// meters the new sensor, and a fan item metered by a fan's duty takes the new fan's own duty as its meter.
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

        var meteredByItsDuty = kind == SensorKind.Fan && MeteredByItsDuty(item);
        return ConfigEdits.Edit(config, id, each =>
        {
            var named = Named(each, sensorId);
            return meteredByItsDuty ? WithDutyOf(named, sensorId, each.Meter?.Metric ?? each.Meter?.Auto)
                : each.Meter is { Sensor: not null } meter && OverlayLayout.MetersItself(each) ? named with { Meter = meter with { Sensor = sensorId } }
                : named;
        });
    }

    /// <summary>
    /// The fan finder's pick: the first item of the overlay that shows the CPU fan names that fan, as
    /// <see cref="ChangeSensor"/> does, so it keeps its meter. No item is added: the finder runs from that item's page.
    /// </summary>
    public static EditResult UseCpuFan(ThermiConfig config, int overlay, Snapshot snapshot, string fanId) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            config.Overlays[overlay].Items.FirstOrDefault(item => (item.Metric ?? item.Auto) == Metrics.CpuFan.Id) is { } fan
                ? ChangeSensor(config, fan.Id, snapshot, fanId)
                : ConfigEdits.Refuse(config, Refusal.NotFound));

    /// <summary>Goes back to the metric the item started from, and its meter to its own metric too.</summary>
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
            Meter = each.Meter is { Auto: { } meter } ? new MeterConfig { Metric = meter } : each.Meter,
        });
    }

    /// <summary>
    /// Sets the meter, or turns it off with null, which takes its lines, alert-only, the graph and the notice with it. A
    /// meter is a temperature, a load, a duty or a fan's speed, and a new one brings its own default lines.
    /// </summary>
    public static EditResult SetMeter(ThermiConfig config, string id, Snapshot snapshot, MeterConfig? meter)
    {
        if (ConfigEdits.Item(config, id) is null)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (meter is null)
        {
            return ConfigEdits.Edit(config, id, Unmetered);
        }

        if (KindOf(meter.Metric ?? meter.Auto, meter.Sensor, snapshot) is not { } kind)
        {
            return ConfigEdits.Refuse(config, Refusal.SensorNotFound);
        }

        return Threshold.CanMeter(kind)
            ? ConfigEdits.Edit(config, id, each => each.Meter == meter ? each : each with { Meter = meter, Threshold = Threshold.Default(meter, snapshot) })
            : ConfigEdits.Refuse(config, Refusal.CannotBeMeter);
    }

    /// <summary>Meters the item by its own value, which only a temperature, a load, a duty or a fan's speed can be.</summary>
    public static EditResult MeterItself(ThermiConfig config, string id, Snapshot snapshot)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        MeterConfig? itself = item switch
        {
            { Metric: { } metric } => new MeterConfig { Metric = metric },
            { Sensor: { } sensor } => new MeterConfig { Sensor = sensor, Auto = item.Auto },
            _ => null,
        };
        return itself is null ? ConfigEdits.Refuse(config, Refusal.CannotMeterItself) : SetMeter(config, id, snapshot, itself);
    }

    /// <summary>
    /// Sets the lines at which the meter warns and is critical, in the unit of what it reads: from 0 to 100, or to 10,000 RPM
    /// for a fan's speed. A line left null is off, and with both off the meter has none.
    /// </summary>
    public static EditResult SetThreshold(ThermiConfig config, string id, double? warning, double? critical)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var max = item.Meter is { } meter ? Threshold.MaxOf(MeterKind(meter)) : 0;
        Refusal? refusal = item.Meter is null ? Refusal.NoMeter
            : !OnTrack(warning, max) || !OnTrack(critical, max) ? Refusal.ThresholdOutOfRange
            : warning > critical ? Refusal.WarningAboveCritical
            : null;
        return refusal is { } refused
            ? ConfigEdits.Refuse(config, refused)
            : ConfigEdits.Edit(config, id, each => each with { Threshold = warning is null && critical is null ? null : new Threshold(warning, critical) });
    }

    /// <summary>Gives the meter back its default lines, which follow what it reads, or none.</summary>
    public static EditResult ResetThreshold(ThermiConfig config, string id, Snapshot snapshot)
    {
        if (ConfigEdits.Item(config, id) is not { } item)
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (item.Meter is not { } meter)
        {
            return ConfigEdits.Refuse(config, Refusal.NoMeter);
        }

        return KindOf(meter.Metric ?? meter.Auto, meter.Sensor, snapshot) is null
            ? ConfigEdits.Refuse(config, Refusal.SensorNotFound)
            : ConfigEdits.Edit(config, id, each => each with { Threshold = Threshold.Default(meter, snapshot) });
    }

    private static bool OnTrack(double? line, double max) => line is not { } value || (value >= 0 && value <= max);

    // A meter's type from the file alone, so its lines' range is known while the service is not read: a duty or a load when
    // the file cannot tell, whose range is 0 to 100 like any meter's but a fan's speed.
    private static SensorKind MeterKind(MeterConfig meter) =>
        Metrics.Find(meter.Metric ?? meter.Auto)?.Kind ?? (meter.Sensor is { } sensor ? SensorMap.KindOfId(sensor) : null) ?? SensorKind.Load;

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
        var (first, last) = PillAround(items, labels, index, config.Overlays[overlay].Style.Layout);
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
    public static (int First, int Last) PillOf(OverlayConfig overlay, int index) =>
        PillAround(overlay.Items, OverlayLayout.RunLabels(overlay.Items), index, overlay.Style.Layout);

    // A pill is a run of one label; with pills, an item that starts a pill of its own breaks the run.
    private static (int First, int Last) PillAround(IReadOnlyList<ItemConfig> items, IReadOnlyList<string?> labels, int index, PillLayout layout)
    {
        if (layout == PillLayout.Each)
        {
            return (index, index);
        }

        var breaks = layout == PillLayout.Pills;
        var first = index;
        while (first > 0 && !(breaks && items[first].Own) && labels[first - 1] == labels[index])
        {
            first--;
        }

        var last = index;
        while (last + 1 < items.Count && !(breaks && items[last + 1].Own) && labels[last + 1] == labels[index])
        {
            last++;
        }

        return (first, last);
    }

    private static bool MeteredByItsDuty(ItemConfig item) => item.Meter is { } meter
        && (meter.Metric == Metrics.CpuFanDuty.Id
            || meter.Auto == Metrics.CpuFanDuty.Id
            || (item.Sensor is { } fan && meter.Sensor == SensorMap.OwnDuty(fan)));

    // An item made from a metric keeps it as auto, so it can go back to it.
    private static ItemConfig Named(ItemConfig item, string sensor) =>
        item with { Metric = null, Sensor = sensor, Sensors = null, Combine = null, Auto = item.Metric ?? item.Auto };

    // A duty the board does not report is still named, so the meter shows as missing rather than another fan's duty.
    private static ItemConfig WithDutyOf(ItemConfig item, string fan, string? auto) => SensorMap.OwnDuty(fan) is { } duty
        ? item with { Meter = new MeterConfig { Sensor = duty, Auto = auto } }
        : Unmetered(item);

    private static ItemConfig Unmetered(ItemConfig item) => item with { Meter = null, Threshold = null, AlertOnly = false, Graph = false, Notify = false };

    /// <summary>The type of what an item shows, or null when none of its sensors is among those the service reads.</summary>
    public static SensorKind? KindOf(ItemConfig item, Snapshot snapshot) =>
        Metrics.Find(item.Metric ?? item.Auto)?.Kind
        ?? SensorsOf(item).Select(sensor => KindOf(metric: null, sensor, snapshot)).FirstOrDefault(kind => kind is not null);

    private static IReadOnlyList<string> SensorsOf(ItemConfig item) => item.Sensors ?? (item.Sensor is { } sensor ? [sensor] : []);

    // A metric says its type even when its sensor is gone; a sensor says it only while the service reads it.
    private static SensorKind? KindOf(string? metric, string? sensor, Snapshot snapshot) =>
        Metrics.Find(metric)?.Kind ?? (sensor is not null && SensorMap.Find(snapshot, sensor) is { } read ? SensorMap.KindOf(read) : null);
}
