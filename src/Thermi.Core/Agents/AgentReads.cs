using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Frames;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Agents;

/// <summary>
/// An agent's read of the samples a call took, oldest first, with the settings: each metric as the first overlay maps it,
/// each sensor by the id an item names it by, and each item as its overlay shows and judges it.
/// </summary>
public static class AgentReads
{
    /// <summary>What an item's id is asked for after, since an item may take a metric's id, as the default items do.</summary>
    public const string ItemPrefix = "item:";

    /// <summary>
    /// The ids asked for, or with none every metric and then every item of every overlay but network addresses, which say
    /// where the PC is to whoever reads the answer, so only an id asks for one; a sensor none of the samples has but
    /// <paramref name="earlier"/> does reads as missing.
    /// </summary>
    public static ReadAnswer Read(IReadOnlyList<Snapshot> samples, ThermiConfig config, IReadOnlyList<string> ids, Snapshot? earlier = null)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException("A read takes at least one sample.", nameof(samples));
        }

        var asked = ids.Count > 0
            ? ids
            : [.. Metrics.All.Select(metric => metric.Id), .. config.Overlays.SelectMany(overlay => overlay.Items).Select(item => ItemPrefix + item.Id)];
        var values = new List<ValueAnswer>();
        var unknown = new List<string>();
        foreach (var id in asked)
        {
            if (Of(samples, config, id, earlier) is { } value)
            {
                values.Add(value);
            }
            else
            {
                unknown.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            values.RemoveAll(value => value.Type == SensorKind.Address);
        }

        return new ReadAnswer(values, unknown);
    }

    /// <summary>The unit a type's values read in, the same whatever the overlay shows them in.</summary>
    public static string UnitOf(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Load or SensorKind.Control => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Power => "W",
        SensorKind.Fan => "RPM",
        SensorKind.Data => "GB",
        SensorKind.Throughput => "B/s",
        SensorKind.Duration or SensorKind.Time => "s",
        SensorKind.Address => "ipv4",
        SensorKind.FrameRate => "fps",
        SensorKind.FrameTime => "ms",
        SensorKind.Voltage => "V",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sensor kind."),
    };

    private static ValueAnswer? Of(IReadOnlyList<Snapshot> samples, ThermiConfig config, string id, Snapshot? earlier)
    {
        var last = samples[^1];
        if (id.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            return ConfigEdits.Find(config, id[ItemPrefix.Length..]) is { } place
                ? OfItem(samples, config, place.Overlay, config.Overlays[place.Overlay].Items[place.Index])
                : null;
        }

        if (Metrics.Find(id) is { } metric)
        {
            var sensor = SensorMap.Chosen(last, config.Overlays[0]).GetValueOrDefault(id);
            var metered = samples.Select(sample => (Reading?)SensorMap.Read(sample, config.Overlays[0])[id]).ToList();
            return Valued(new ValueAnswer(id, ValueSource.Metric, Texts.English.Of(metric.Name), metric.Kind, UnitOf(metric.Kind)), metered, sensor);
        }

        // A sensor that dropped out of the last samples is still the one asked for, with no value now.
        var found = samples.Reverse().Select(sample => SensorMap.Find(sample, id)).FirstOrDefault(each => each is not null)
            ?? (earlier is null ? null : SensorMap.Find(earlier, id));
        if (found is null || SensorMap.ReadingOf(found) is not { } reading)
        {
            return null;
        }

        var readings = samples.Select(sample => SensorMap.Find(sample, id) is { } each ? SensorMap.ReadingOf(each) : null).ToList();
        return Valued(new ValueAnswer(id, ValueSource.Sensor, found.Name, reading.Kind, UnitOf(reading.Kind)), readings, found) with { Sensor = null };
    }

    private static ValueAnswer OfItem(IReadOnlyList<Snapshot> samples, ThermiConfig config, int at, ItemConfig item)
    {
        var overlay = config.Overlays[at];
        var last = samples[^1];
        var now = SensorMap.Read(last, overlay);
        var readings = samples.Select(sample => OverlayLayout.ReadingOf(item, SensorMap.Read(sample, overlay))).ToList();
        var kind = readings[^1]?.Kind ?? Threshold.KindOf(item) ?? SensorKind.Load;
        var shown = OverlayLayout.Build(overlay with { Items = [item] }, now, CultureInfo.InvariantCulture, temperature: config.Temperature, windowOpen: true);
        var judged = Threshold.Judges(item) ? item.Threshold : null;
        var sensor = item.Sensor is { } id ? SensorMap.Find(last, id)
            : item.Metric is { } metric ? SensorMap.Chosen(last, overlay).GetValueOrDefault(metric)
            : null;
        var answer = new ValueAnswer(item.Id, ValueSource.Item, item.Name ?? Names.OfUnnamed(item, last, Texts.English), kind, UnitOf(kind))
        {
            Overlay = at,
            Shown = shown is [var drawn] ? drawn.Value : null,
            Level = shown is [var level] ? level.Level : Level.Normal,
            Warning = judged?.Warning,
            Critical = judged?.Critical,
        };
        var valued = Valued(answer, readings, sensor);
        // A reader of no frames, as Linux's, sends neither the game's sensors nor the slots they are made of, so nothing of the
        // item's says why it reads nothing.
        var unread = GameSensors.Unread(last) && !last.Sensors.Any(FrameSlots.Is);
        return valued.Value is null && OverlayLayout.ReadsFrames(item) && unread ? valued with { Why = NoValue.FramesUnread } : valued;
    }

    private static ValueAnswer Valued(ValueAnswer answer, List<Reading?> readings, RawSensor? sensor)
    {
        var last = readings[^1];
        var usable = readings.Select(reading => reading?.Usable).OfType<double>().ToList();
        var why = sensor is not null && last is { } read ? SensorCatalog.Why(sensor, read) : last?.Usable is null ? NoValue.ReadsNothing : null;
        // An address taken as a number has no minimum or average to tell.
        var many = readings.Count > 1 && usable.Count > 0 && answer.Type != SensorKind.Address;
        return answer with
        {
            Sensor = sensor?.Id,
            Device = sensor?.Hardware,
            Value = answer.Type == SensorKind.Address ? null : last?.Usable,
            Text = answer.Type == SensorKind.Address && last?.Usable is { } address ? SystemSensors.AddressText(address) : null,
            Why = why,
            Samples = usable.Count,
            Min = many ? usable.Min() : null,
            Max = many ? usable.Max() : null,
            Average = many ? usable.Average() : null,
        };
    }

}
