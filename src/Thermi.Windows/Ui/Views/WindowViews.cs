using System.Globalization;
using Thermi.Core.Pipe;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Ui.Editor;
using Thermi.Windows.Win32;

namespace Thermi.Windows.Ui.Views;

/// <summary>
/// What the page reads of the overlays and the sensors, as Core decides it: each overlay's pills, each item's value and
/// level, every sensor's value, and the catalog with each sensor's last minute since the window opened. Values show with
/// their units in the regional format, whatever the overlay's style hides. Used on the window's thread alone.
/// </summary>
/// <param name="editor">Whose settings are shown.</param>
/// <param name="sensors">The readings now, null while none are fresh.</param>
/// <param name="readings">What the window has read since it opened.</param>
/// <param name="history">The window's history of its items, which takes the overlay's, so each value is the overlay's.</param>
internal sealed class WindowViews(WindowEditor editor, ISensorFeed sensors, Readings readings, ItemHistory history)
{
    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    /// <summary>A fresh snapshot, kept for the catalog's last minute.</summary>
    public void Arrived()
    {
        if (sensors.Latest is { } snapshot && editor.Config.Overlays is [var first, ..])
        {
            readings.Record(snapshot, first);
        }
    }

    public WindowView View(Texts texts)
    {
        var config = editor.Config;
        var snapshot = sensors.Latest;
        var known = readings.Known(snapshot);
        var catalog = readings.CatalogOf(known);
        var pills = new List<IReadOnlyList<PillView>>();
        var items = new Dictionary<string, ItemView>(StringComparer.Ordinal);
        foreach (var overlay in config.Overlays)
        {
            var now = snapshot is null ? NoReadings : readings.ReadOf(snapshot, overlay);
            var shown = OverlayLayout.Build(
                overlay with { Style = overlay.Style with { Units = true } },
                now,
                CultureInfo.CurrentCulture,
                texts,
                config.Temperature,
                history,
                windowOpen: true);
            var runs = OverlayLayout.Runs(overlay, texts);
            var labels = runs.Select(run => LabelOf(ItemsOf(overlay, run), run, shown)).ToList();
            pills.Add([.. runs.Select((run, at) => new PillView(labels[at] ?? "", [.. ItemsOf(overlay, run).Select(item => item.Id)]))]);
            var chosen = readings.ChosenOf(known, overlay);
            foreach (var (run, at) in runs.Select((run, at) => (run, at)))
            {
                foreach (var item in ItemsOf(overlay, run))
                {
                    items[item.Id] = ItemOf(item, shown.FirstOrDefault(each => each.Id == item.Id), (known, catalog), chosen, (texts, config.SensorNames)) with
                    {
                        Tag = OverlayLayout.TagOf(item, CultureInfo.CurrentCulture, texts),
                        Label = labels[at] ?? OverlayLayout.LabelOf(item, texts),
                    };
                }
            }
        }

        return new WindowView(pills, items, Values(snapshot, Format(texts)), Mark(config, snapshot, texts));
    }

    /// <summary>
    /// The catalog's devices and sensors for a search and the types chosen, and unless <paramref name="all"/>, without
    /// those nobody would add.
    /// </summary>
    public CatalogView Catalog(string search, IReadOnlyList<SensorKind> kinds, bool all, Texts texts)
    {
        var devices = sensors.Latest is { } snapshot ? SensorCatalog.Build(snapshot, editor.Config.SensorNames, texts) : [];
        var found = SensorCatalog.Filter(devices, search, kinds, texts);
        var (shown, hidden) = all ? (found, 0) : SensorCatalog.Hide(found, readings.Seen);
        var format = Format(texts);
        var groups = shown
            .Select(device => new CatalogGroup(device.Id, device.Name, device.Kind, [.. device.Sensors.Select(sensor => Row(sensor, format))], device.Marks))
            .ToList();
        var count = groups.Sum(group => group.Sensors.Count);
        return new CatalogView(groups, count, hidden, count + hidden == 0 ? SensorCatalog.Suggest(devices, search, texts) : null);
    }

    /// <summary>Where each overlay shows on <paramref name="screens"/>, as its own window places it while the window is open.</summary>
    public IReadOnlyList<Rect?> Places(Texts texts, IReadOnlyList<Screen> screens) => OverlayPlaces.Of(editor.Config, Input(texts), screens);

    /// <summary>
    /// An edit's settings with each overlay it pushed along a taskbar keeping the place it was pushed to, as the overlays show
    /// it, or why they cannot be.
    /// </summary>
    public EditResult Settled(ThermiConfig before, ThermiConfig after, Texts texts, IReadOnlyList<Screen> screens) =>
        OverlayPlaces.Settled(before, after, Input(texts), screens);

    private TrayInput Input(Texts texts) =>
        new(sensors.Latest, sensors.State, CultureInfo.CurrentCulture, texts, LightTaskbar: false, ExePath: "") { WindowOpen = true };

    /// <summary>Numbers in the regional format, texts in the window's language, temperatures in the unit the settings choose.</summary>
    public ValueFormat Format(Texts texts) => new(texts, CultureInfo.CurrentCulture, editor.Config.Temperature) { Known = readings.Known(sensors.Latest) };

    /// <summary>A reading as the window writes it, with its unit; a missing one as the overlay's dash.</summary>
    public static string Text(Reading reading, ValueFormat format) =>
        ReadingFormatter.Format(reading, format.Numbers, temperature: format.Temperature, texts: format.Texts);

    // The tray's mark, decided as the tray decides it; the taskbar's mode and thermi's path only shape the item icons.
    private MarkState Mark(ThermiConfig config, Snapshot? snapshot, Texts texts) =>
        ThermiMark.Of(
            config,
            new TrayInput(snapshot, sensors.State, CultureInfo.CurrentCulture, texts, LightTaskbar: false, ExePath: ""),
            Desktop.HighContrast() is not null);

    private CatalogRow Row(CatalogSensor sensor, ValueFormat format) => new(
        sensor.Id,
        sensor.Name.Shown,
        sensor.Name.From,
        sensor.Reading.Kind,
        sensor.Reading.Usable is null ? null : Text(sensor.Reading, format),
        sensor.Why,
        readings.History.LastMinute(sensor.Id),
        SensorNames.NoteOf(sensor.Name, format.Texts, format.Numbers),
        Tip(sensor.Name, format),
        sensor.Found?.Title ?? [],
        sensor.Found?.Detail ?? [],
        sensor.Found?.Alias,
        sensor.Found?.AliasMarks ?? []);

    private static string? Tip(SensorName name, ValueFormat format) => name switch
    {
        { From: NameFrom.Likely } => format.Texts.Format(format.Numbers, CatalogText.LikelyTip, name.Chip, name.Library, name.Shown),
        { From: NameFrom.Library, Chip: { } chip } => format.Texts.Format(format.Numbers, CatalogText.InputTip, chip),
        _ => null,
    };

    private Dictionary<string, string> Values(Snapshot? snapshot, ValueFormat format) =>
        readings.CatalogOf(snapshot)
            .SelectMany(device => device.Sensors)
            .Where(sensor => sensor.Reading.Usable is not null)
            .ToDictionary(sensor => sensor.Id, sensor => Text(sensor.Reading, format), StringComparer.Ordinal);

    private static IEnumerable<ItemConfig> ItemsOf(OverlayConfig overlay, Run run) => overlay.Items.Skip(run.First).Take(run.Last - run.First + 1);

    // A pill is drawn under the label its first value shown carries, with that value's tag; under its run's own while none shows.
    private static string? LabelOf(IEnumerable<ItemConfig> pill, Run run, IReadOnlyList<OverlayItem> shown) =>
        pill.Select(item => shown.FirstOrDefault(each => each.Id == item.Id)?.Label).FirstOrDefault(label => label is not null) ?? run.Label;

    // A value of the dash alone, an arrow before it or not, reads nothing; a level needs a reading lines judge and a value.
    private static ItemView ItemOf(
        ItemConfig item,
        OverlayItem? shown,
        (Snapshot Known, IReadOnlyList<CatalogDevice> Catalog) from,
        IReadOnlyDictionary<string, RawSensor?> chosen,
        (Texts Texts, IReadOnlyDictionary<string, string>? Own) names)
    {
        var known = from.Known;
        var value = shown is { Value: var text } && !text.EndsWith(ReadingFormatter.Missing, StringComparison.Ordinal) ? text : null;
        var auto = (item.Metric ?? item.Auto) is { } metric ? chosen.GetValueOrDefault(metric)?.Id : null;
        var kind = ItemEdits.KindOf(item, known);
        var read = item.Sensor ?? (item.Sensors is [var first, ..] ? first : auto);
        var sensor = read is null ? null : SensorMap.Find(known, read);
        var type = sensor?.Type ?? (kind is { } some ? TypeOf(some) : "");
        // What else an item warns by is a reading of the item itself, so each is named by what it reads: a fan's speed or its
        // duty, else its type; its own reading comes first, Off for one that lines cannot judge, as the RAM in use.
        IReadOnlyList<WarnByChoice> warnBy =
        [
            new(null, names.Texts.Of(kind is { } own && Threshold.CanMeter(own) ? UiText.WarnByOwn : UiText.WarnByOff)),
            .. Threshold.WarnByOf(item, kind, known)
                .Select(other => new WarnByChoice(other, KindName(Threshold.KindOf(other) ?? SensorKind.Load, names.Texts))),
        ];
        var judged = Threshold.Judges(item);
        Level? level = !judged || value is null ? null : shown!.Level;
        return new ItemView(value, level, type, auto, warnBy, WarnByName(item.WarnBy, known, names.Texts, names.Own))
        {
            Judged = judged,
            Bits = kind == SensorKind.Throughput
                && (sensor?.HardwareType == "Network"
                    || (item.Metric ?? item.Auto) == Metrics.NetDownload.Id
                    || (item.Metric ?? item.Auto) == Metrics.NetUpload.Id),
            Lit = shown?.Lit,
            OffsetShown = OffsetOf(item, kind, names.Texts),
            Offer = SensorOffer.Of(from.Catalog, type, read, item.Sensor),
        };
    }

    // The file keeps an offset in °C, so it is typed and shown in °C whatever the overlay shows.
    private static string? OffsetOf(ItemConfig item, SensorKind? kind, Texts texts) =>
        kind is { } some ? ReadingFormatter.Format(some, item.Offset, new ValueFormat(texts, CultureInfo.CurrentCulture), units: true, difference: true) : null;

    // A sensor of its own is named as the catalog names it, one the service no longer reads by the user's name or its id;
    // else its metric's name.
    private static string? WarnByName(WarnByConfig? warnBy, Snapshot known, Texts texts, IReadOnlyDictionary<string, string>? own) =>
        warnBy is null ? null
        : warnBy.Sensor is { } sensor
            ? (SensorMap.Find(known, sensor) is { } found
                ? SensorNames.Of(found, sensor, own, texts).Shown
                : own?.GetValueOrDefault(sensor) ?? sensor)
        : Metrics.Find(warnBy.Metric ?? warnBy.Auto) is { } metric ? texts.Of(metric.Name)
        : warnBy.Metric;

    private static string KindName(SensorKind kind, Texts texts) => kind == SensorKind.Fan ? texts.Of(UiText.WarnBySpeed) : texts.Of(kind);

    // The library's name of a type, which the page compares sensors by: a duration is a TimeSpan there.
    private static string TypeOf(SensorKind kind) => kind == SensorKind.Duration ? "TimeSpan" : kind.ToString();
}
