using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// The names and places of what an edit makes. A new item is named like the defaults, as "GPU temperature", with its
/// sensor's own name in brackets when another item has that name, and a number when the same sensor is added again; it
/// joins the pill of its device's kind. An overlay without a typed name is named from where it sits, with a number when one reads the same.
/// </summary>
internal static class Names
{
    // The longest label a sensor of no known kind of device gets from its name; one typed by hand can be longer.
    private const int SensorLabelLength = 8;

    private static readonly SnapSpot[] Corners = [SnapSpot.TopRight, SnapSpot.BottomRight, SnapSpot.BottomLeft, SnapSpot.TopLeft];

    /// <summary>The name of a new item that shows <paramref name="sensor"/>, under <paramref name="sensorId"/>.</summary>
    public static string OfItem(ThermiConfig config, RawSensor sensor, string sensorId, Texts texts)
    {
        var taken = config.Overlays.SelectMany(overlay => overlay.Items).Where(item => item.Name is not null).ToList();
        var name = Base(config, sensor, sensorId, texts);
        if (taken.Exists(item => item.Name == name) && !taken.Exists(item => item.Name == name && item.Sensor == sensorId))
        {
            name = texts.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, name, sensor.Name);
        }

        return Numbered(name, [.. taken.Select(item => item.Name)], texts);
    }

    /// <summary>
    /// <paramref name="renamed"/>, whose sensor <paramref name="sensorId"/> has a new name, with the items that show that
    /// sensor alone under the name thermi gave them in <paramref name="before"/> named as a new item of it would be now;
    /// an item renamed by hand, or made from a metric, keeps its name.
    /// </summary>
    public static ThermiConfig Renamed(ThermiConfig before, ThermiConfig renamed, RawSensor sensor, string sensorId, Texts texts)
    {
        var count = before.Overlays.Sum(overlay => overlay.Items.Count) + 1;
        var old = Base(before, sensor, sensorId, texts);
        var given = new[] { old, texts.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, old, sensor.Name) }
            .SelectMany(form => Enumerable.Range(2, count).Select(number => texts.Format(CultureInfo.InvariantCulture, NameText.Numbered, form, number)).Prepend(form))
            .ToHashSet(StringComparer.Ordinal);
        var ours = before.Overlays.SelectMany(overlay => overlay.Items)
            .Where(item => item is { Metric: null, Auto: null, Sensors: null } && item.Sensor == sensorId && item.Name is { } itemName && given.Contains(itemName))
            .Select(item => item.Id)
            .ToList();
        var name = Base(renamed, sensor, sensorId, texts);
        var config = renamed;
        foreach (var id in ours)
        {
            var taken = config.Overlays.SelectMany(overlay => overlay.Items).Where(item => item.Id != id).Select(item => item.Name).ToList();
            config = ConfigEdits.WithItem(config, id, item => item with { Name = Numbered(name, taken, texts) });
        }

        return config;
    }

    /// <summary>
    /// The name of an item a file left without one, as a new item of it would be named: its metric's, else its sensor's
    /// while the snapshot knows it, else its id.
    /// </summary>
    public static string OfUnnamed(ItemConfig item, Snapshot? known, Texts texts) =>
        Metrics.Find(item.Metric ?? item.Auto) is { } metric ? texts.Of(metric.Name)
        : (item.Sensor ?? (item.Sensors is [var first, ..] ? first : null)) is { } id && known is not null && SensorMap.Find(known, id) is { } sensor ? Base(sensor, texts)
        : item.Id;

    /// <summary>
    /// Every overlay's name, in order: the one the user typed, else where it sits now, in <paramref name="texts"/>'
    /// language, with a number when an earlier one reads the same. Only a typed name is saved, so moving an overlay or
    /// changing the language renames the others.
    /// </summary>
    public static IReadOnlyList<string> OfOverlays(ThermiConfig config, Texts texts)
    {
        var shown = new List<string>();
        var typed = config.Overlays.Select(each => each.Name).OfType<string>().ToList();
        foreach (var overlay in config.Overlays)
        {
            var place = texts.Of(overlay.Snapped?.Spot switch
            {
                null => NameText.Floating,
                SnapSpot.TopLeft => NameText.TopLeft,
                SnapSpot.TopRight => NameText.TopRight,
                SnapSpot.BottomLeft => NameText.BottomLeft,
                SnapSpot.BottomRight => NameText.BottomRight,
                SnapSpot.TaskbarLeft or SnapSpot.TaskbarCenter or SnapSpot.TaskbarRight or _ => NameText.Taskbar,
            });
            shown.Add(overlay.Name ?? Numbered(place, [.. typed, .. shown], texts));
        }

        return shown;
    }

    /// <summary>The first corner of the primary monitor no overlay snaps to, in the order top right, bottom right, bottom left, top left.</summary>
    public static SnapSpot FreeCorner(ThermiConfig config) =>
        Corners.FirstOrDefault(corner => !config.Overlays.Any(overlay => overlay.Snap is { Monitor: null } snap && snap.Spot == corner), SnapSpot.TopRight);

    /// <summary>
    /// Where a sensor's item goes in an overlay, and its label: after the last item of the pill of its device's kind, taking
    /// that pill's label, or at the end with its kind's label; a sensor of a metric whose label is its own, as a drive's
    /// read and write rates, does the same by that label. A sensor of no kind thermi knows is labeled from its name, cut short.
    /// </summary>
    public static (int At, string Label) PlaceOf(OverlayConfig overlay, RawSensor sensor, Texts texts)
    {
        var items = overlay.Items;
        if (SensorMap.DeviceOf(sensor) is not { } device)
        {
            var name = sensor.Name.Trim().ToUpperInvariant();
            return (items.Count, name[..Math.Min(name.Length, SensorLabelLength)].TrimEnd());
        }

        var labels = OverlayLayout.RunLabels(items);
        var metric = SensorMap.MetricOf(sensor);
        var (english, own) = metric is null ? (Texts.English.Of(device), texts.Of(device)) : (Texts.English.Of(metric.Label), texts.Of(metric.Label));
        var last = labels.ToList().FindLastIndex(label => label == english || label == own);
        return last < 0 ? (items.Count, own) : (ItemEdits.PillOf(overlay, last).Last + 1, labels[last]!);
    }

    // A sensor the user named gives its items that name.
    private static string Base(ThermiConfig config, RawSensor sensor, string sensorId, Texts texts) =>
        config.SensorNames?.GetValueOrDefault(sensorId) ?? Base(sensor, texts);

    private static string Base(RawSensor sensor, Texts texts)
    {
        if (SensorMap.DeviceOf(sensor) is not { } device || SensorMap.KindOf(sensor) is not { } kind)
        {
            return sensor.Name;
        }

        if (SensorMap.MetricOf(sensor) is { } like)
        {
            return texts.Of(like.Name);
        }

        var name = texts.Format(CultureInfo.InvariantCulture, NameText.OfSensor, texts.Of(device), texts.Of(kind).ToLowerInvariant());
        return string.Concat(name[..1].ToUpperInvariant(), name[1..]);
    }

    private static string Numbered(string name, IReadOnlyCollection<string?> taken, Texts texts)
    {
        var number = 1;
        var candidate = name;
        while (taken.Contains(candidate))
        {
            number++;
            candidate = texts.Format(CultureInfo.InvariantCulture, NameText.Numbered, name, number);
        }

        return candidate;
    }
}
