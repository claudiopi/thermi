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

    private const string RamWord = "Memory";

    private static readonly SnapSpot[] Corners = [SnapSpot.TopRight, SnapSpot.BottomRight, SnapSpot.BottomLeft, SnapSpot.TopLeft];

    /// <summary>
    /// The name of a new item that shows <paramref name="sensor"/>, under <paramref name="sensorId"/>. An item of the metric
    /// that picks that very sensor by name shows the same sensor, so the new one is numbered, not told apart by its sensor.
    /// </summary>
    public static string OfItem(ThermiConfig config, RawSensor sensor, string sensorId, Texts texts)
    {
        var picked = SensorMap.PickedBy(sensor)?.Id;
        var taken = config.Overlays.SelectMany(overlay => overlay.Items)
            .Select(item => item with { Name = item.Name ?? (Metrics.Find(item.Metric ?? item.Auto) is { } metric ? texts.Of(metric.Name) : null) })
            .Where(item => item.Name is not null)
            .ToList();
        var name = Base(config, sensor, sensorId, texts);
        bool Same(ItemConfig item)
        {
            return item.Sensor == sensorId || (item is { Sensor: null, Sensors: null } && picked is not null && (item.Metric ?? item.Auto) == picked);
        }

        if (taken.Exists(item => item.Name == name) && !taken.Exists(item => item.Name == name && Same(item)))
        {
            // A name that already says the sensor's own is told apart by its device instead.
            var apart = name.Contains(sensor.Name, StringComparison.OrdinalIgnoreCase) ? sensor.Hardware : sensor.Name;
            name = texts.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, name, apart);
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
        var given = new[] { sensor.Name, sensor.Hardware }
            .Select(apart => texts.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, old, apart))
            .Prepend(old)
            .SelectMany(form => Enumerable.Range(2, count)
                .Select(number => texts.Format(CultureInfo.InvariantCulture, NameText.Numbered, form, number))
                .Prepend(form))
            .ToHashSet(StringComparer.Ordinal);
        var ours = before.Overlays.SelectMany(overlay => overlay.Items)
            .Where(item => item is { Metric: null, Auto: null, Sensors: null }
                && item.Sensor == sensorId
                && item.Name is { } itemName
                && given.Contains(itemName))
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
        : (item.Sensor ?? (item.Sensors is [var first, ..] ? first : null)) is { } id
            && known is not null
            && SensorMap.Find(known, id) is { } sensor ? Base(sensor, texts)
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
                SnapSpot.Taskbar or _ => NameText.Taskbar,
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
    /// that pill's label, or at the end with its kind's label; a sensor of a metric does the same by its metric's label, as
    /// the board's CPU fan and its duty join the CPU's pill, and Windows' uptime takes UP. A drive's read and write rates
    /// join that drive's pill. A sensor of no kind thermi knows is labeled from its name, cut short.
    /// </summary>
    public static (int At, string Label) PlaceOf(OverlayConfig overlay, RawSensor sensor, Texts texts, string? label = null)
    {
        var items = overlay.Items;
        if (label is not null)
        {
            var run = OverlayLayout.RunLabels(items).ToList().FindLastIndex(each => each == label);
            return (run < 0 ? items.Count : ItemEdits.PillOf(overlay, run).Last + 1, label);
        }

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

    // Named as its metric, else by its device and kind where the kind says what it reads, as "SSD temperature", else by the
    // name the catalog gives it, after its device unless that name says it: "SSD free space", "GPU Fan duty", "CPU fan".
    private static string Base(RawSensor sensor, Texts texts)
    {
        if (SensorMap.MetricOf(sensor) is { } like)
        {
            return texts.Of(like.Name);
        }

        var shown = SensorNames.Of(sensor, "", texts: texts).Shown;
        if (SensorMap.DeviceOf(sensor) is not { } device || SensorMap.KindOf(sensor) is not { } kind)
        {
            return shown;
        }

        var kindName = texts.Of(kind).ToLowerInvariant();
        if (SensorNames.LikelyOf(sensor) is not null || device == DeviceKind.Board)
        {
            return kind == SensorKind.Temperature && SensorNames.LikelyOf(sensor) is not null ? Of(shown, kindName, texts) : shown;
        }

        var deviceName = texts.Of(device);
        return Says(kind, device) ? Capital(Of(deviceName, kindName, texts))
            : shown.StartsWith(deviceName, StringComparison.OrdinalIgnoreCase) ? shown
            : OfMemory(sensor, shown, device) ?? Of(deviceName, Lowercase(shown), texts);
    }

    // The library calls each of its memories "Memory", which the memory's own name says better: "Virtual memory used".
    private static string? OfMemory(RawSensor sensor, string shown, DeviceKind device) =>
        device == DeviceKind.Ram && (shown == RamWord || shown.StartsWith(RamWord + " ", StringComparison.Ordinal))
            ? Capital(Lowercase(sensor.Hardware + shown[RamWord.Length..]))
            : null;

    private static string Lowercase(string name) => string.Join(' ', name.Split(' ').Select(Lowered));

    // A temperature, power, frequency or fan speed is what it reads wherever it is; a load only on a CPU or a GPU, since a
    // drive's "load" is its used space or its activity.
    private static bool Says(SensorKind kind, DeviceKind device) =>
        kind is SensorKind.Temperature or SensorKind.Power or SensorKind.Clock or SensorKind.Fan
        || (kind == SensorKind.Load && device is DeviceKind.Cpu or DeviceKind.Gpu);

    private static string Of(string device, string what, Texts texts) => texts.Format(CultureInfo.InvariantCulture, NameText.OfSensor, device, what);

    private static string Capital(string name) => string.Concat(name[..1].ToUpperInvariant(), name[1..]);

    // A word written as a title, as "Space", reads in lower case in the middle of a name; one in capitals, as "PCIe", stays.
    private static string Lowered(string word) =>
        word.Length > 2 && char.IsUpper(word[0]) && word[1..].All(char.IsLower) ? word.ToLowerInvariant() : word;

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
