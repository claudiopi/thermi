using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Ui.Editor;
using Thermi.Windows;

namespace Thermi.Ui.Views;

/// <summary>
/// What the page reads of the overlays and the sensors, as Core decides it: each overlay's pills, each item's value and
/// level, every sensor's value, and the catalog with each sensor's last minute since the window opened. Values show with
/// their units in the regional format, whatever the overlay's style hides. Used on the window's thread alone.
/// </summary>
/// <param name="editor">Whose settings are shown.</param>
/// <param name="sensors">The readings now, null while none are fresh.</param>
/// <param name="readings">What the window has read since it opened.</param>
internal sealed class WindowViews(WindowEditor editor, ISensorFeed sensors, Readings readings)
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
        var pills = new List<IReadOnlyList<PillView>>();
        var items = new Dictionary<string, ItemView>(StringComparer.Ordinal);
        foreach (var overlay in config.Overlays)
        {
            var now = snapshot is null ? NoReadings : SensorMap.Read(snapshot, overlay);
            var shown = OverlayLayout.Build(overlay with { Style = overlay.Style with { Units = true } }, now, CultureInfo.CurrentCulture, texts, config.Temperature, windowOpen: true);
            pills.Add([.. OverlayLayout.Groups(shown, overlay.Style.Layout).Select(pill => new PillView(pill[0].Label ?? "", [.. pill.Select(item => item.Id)]))]);
            var chosen = SensorMap.Chosen(known, overlay);
            foreach (var item in overlay.Items)
            {
                items[item.Id] = ItemOf(item, shown.FirstOrDefault(each => each.Id == item.Id), known, chosen, (texts, config.SensorNames));
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
        var groups = shown.Select(device => new CatalogGroup(device.Id, device.Name, device.Kind, [.. device.Sensors.Select(sensor => Row(sensor, format))])).ToList();
        var count = groups.Sum(group => group.Sensors.Count);
        return new CatalogView(groups, count, hidden, count + hidden == 0 ? SensorCatalog.Suggest(devices, search, texts) : null);
    }

    /// <summary>Numbers in the regional format, texts in the window's language, temperatures in the unit the settings choose.</summary>
    public ValueFormat Format(Texts texts) => new(texts, CultureInfo.CurrentCulture, editor.Config.Temperature);

    /// <summary>A reading as the window writes it, with its unit; a missing one as the overlay's dash.</summary>
    public static string Text(Reading reading, ValueFormat format) =>
        ReadingFormatter.Format(reading, format.Numbers, temperature: format.Temperature, texts: format.Texts);

    // The tray's mark, decided as the tray decides it; the taskbar's mode and thermi's path only shape the item icons.
    private MarkState Mark(ThermiConfig config, Snapshot? snapshot, Texts texts) =>
        ThermiMark.Of(config, new TrayInput(snapshot, sensors.State, CultureInfo.CurrentCulture, texts, LightTaskbar: false, ExePath: ""), Desktop.HighContrast() is not null);

    private CatalogRow Row(CatalogSensor sensor, ValueFormat format) => new(
        sensor.Id,
        sensor.Name.Shown,
        sensor.Name.From,
        sensor.Reading.Kind,
        sensor.Reading.Usable is null ? null : Text(sensor.Reading, format),
        sensor.Why,
        readings.History.LastMinute(sensor.Id),
        SensorNames.NoteOf(sensor.Name, format.Texts, format.Numbers),
        Tip(sensor.Name, format));

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

    // A value of the dash alone, an arrow before it or not, reads nothing; a level needs a meter and a value.
    private static ItemView ItemOf(ItemConfig item, OverlayItem? shown, Snapshot known, IReadOnlyDictionary<string, RawSensor?> chosen, (Texts Texts, IReadOnlyDictionary<string, string>? Own) names)
    {
        var value = shown is { Value: var text } && !text.EndsWith(ReadingFormatter.Missing, StringComparison.Ordinal) ? text : null;
        var auto = (item.Metric ?? item.Auto) is { } metric ? chosen.GetValueOrDefault(metric)?.Id : null;
        var kind = ItemEdits.KindOf(item, known);
        var read = item.Sensor ?? (item.Sensors is [var first, ..] ? first : auto);
        var type = (read is null ? null : SensorMap.Find(known, read)?.Type) ?? (kind is { } some ? TypeOf(some) : "");
        // A meter is a reading of the item itself, so each is named by what it reads: a fan's speed or its duty, else its type.
        IReadOnlyList<MeterChoice> meters = [.. Threshold.MetersOf(item, kind, known).Select((meter, at) =>
            new MeterChoice(meter, MeterKindName(at == 0 ? kind!.Value : kind == SensorKind.Fan ? SensorKind.Control : SensorKind.Fan, names.Texts)))];
        return new ItemView(value, item.Meter is null || value is null ? null : shown!.Level, type, auto, meters, MeterName(item.Meter, known, names.Texts, names.Own));
    }

    // A sensor of its own is named as the catalog names it, one the service no longer reads by the user's name or its id;
    // else its metric's name.
    private static string? MeterName(MeterConfig? meter, Snapshot known, Texts texts, IReadOnlyDictionary<string, string>? own) =>
        meter is null ? null
        : meter.Sensor is { } sensor ? (SensorMap.Find(known, sensor) is { } found ? SensorNames.Of(found, sensor, own, texts).Shown : own?.GetValueOrDefault(sensor) ?? sensor)
        : Metrics.Find(meter.Metric ?? meter.Auto) is { } metric ? texts.Of(metric.Name)
        : meter.Metric;

    private static string MeterKindName(SensorKind kind, Texts texts) => kind == SensorKind.Fan ? texts.Of(UiText.MeterSpeed) : texts.Of(kind);

    // The library's name of a type, which the page compares sensors by: a duration is a TimeSpan there.
    private static string TypeOf(SensorKind kind) => kind == SensorKind.Duration ? "TimeSpan" : kind.ToString();
}
