using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>
/// thermi's mark: asleep while all is well, awake at a warning, alarmed at critical, gray while sensors are stopped or
/// stale, one color under high contrast; and awake, cool to warm, as the program's own icon, which no tray shows.
/// </summary>
public enum MarkState
{
    Calm,
    Warm,
    Hot,
    Stopped,
    HighContrast,
    App,
}

/// <summary>What a value's tooltip adds: why it is missing and what to do, or the first-run hint.</summary>
public enum ValueHint
{
    None,
    NeedsDriver,
    SensorsStopped,
    ReadsNothing,
    SetUp,
}

/// <summary>A value's tooltip: the item it is of, or null for the "Sensors stopped" pill, its level, and its hint.</summary>
public sealed record ValueTooltip(string? ItemId, Level Level, ValueHint Hint);

/// <summary>What an overlay shows: its items, or one "Sensors stopped" pill with thermi's mark, and each value's tooltip.</summary>
public sealed record OverlayScene(IReadOnlyList<OverlayItem> Items, bool SensorsStopped, IReadOnlyList<ValueTooltip> Tooltips);

/// <summary>
/// thermi's mark and its tooltip, and each overlay's scene, from the settings and the tray's input. The mark and the
/// tooltip take every item, those of hidden overlays included, since they tell how the PC is, not what shows.
/// </summary>
public static class ThermiMark
{
    private const string Name = "thermi";
    private const string Separator = " · ";

    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    public static MarkState Of(ThermiConfig config, TrayInput input, bool highContrast)
    {
        if (highContrast)
        {
            return MarkState.HighContrast;
        }

        if (Live(input) is not { } snapshot)
        {
            return MarkState.Stopped;
        }

        var levels = config.Overlays.SelectMany(overlay =>
        {
            var readings = SensorMap.Read(snapshot, overlay);
            return overlay.Items.Select(item => OverlayLayout.LevelOf(item, OverlayLayout.MeterOf(item, readings)));
        });
        var worst = levels.DefaultIfEmpty(Level.Normal).Max();
        return worst == Level.Critical ? MarkState.Hot : worst == Level.Warning ? MarkState.Warm : MarkState.Calm;
    }

    /// <summary>
    /// An overlay's scene. While the service is stopped, one pill stands in for its items; while its data is stale, the
    /// items show as missing. A missing value says why: the driver when a CPU temperature reads zero, else that it reads
    /// nothing. Until the window has opened once, a value's tooltip offers to set thermi up.
    /// </summary>
    public static OverlayScene Overlay(ThermiConfig config, int overlay, TrayInput input, bool windowOpenedOnce)
    {
        if (input.Service == ServiceState.Stopped)
        {
            return new OverlayScene([], SensorsStopped: true, [new ValueTooltip(null, Level.Normal, ValueHint.SensorsStopped)]);
        }

        var shown = config.Overlays[overlay];
        var snapshot = Live(input);
        var readings = snapshot is null ? NoReadings : SensorMap.Read(snapshot, shown);
        var built = OverlayLayout.Build(shown, readings, input.Numbers, input.Texts, config.Temperature, input.History, input.WindowOpen);
        var driver = snapshot is not null && SensorMap.DriverMayBeMissing(snapshot);
        var throttled = snapshot is not null && Throttling.Of(snapshot, shown, input.BaseClock) is not null;
        var configs = built.Select(drawn => shown.Items.First(item => item.Id == drawn.Id)).ToList();
        var tooltips = configs.Select(item => new ValueTooltip(
            item.Id,
            OverlayLayout.LevelOf(item, OverlayLayout.MeterOf(item, readings)),
            snapshot is null ? ValueHint.SensorsStopped : HintOf(OverlayLayout.ReadingOf(item, readings), driver, windowOpenedOnce))).ToList();
        var items = built.Select((drawn, at) => snapshot is null ? drawn : Marked(drawn, configs[at], tooltips[at].Hint, throttled, snapshot, input.Texts));
        return new OverlayScene([.. items], SensorsStopped: false, tooltips);
    }

    /// <summary>Whether an item reads one of the CPU's temperatures, powers or clocks, which read zero without thermi's driver.</summary>
    internal static bool ReadsWithTheDriver(ItemConfig item, Snapshot snapshot) =>
        (Metrics.Find(item.Metric ?? item.Auto) is { } metric
            && Metrics.DeviceOf(metric) == DeviceKind.Cpu
            && metric.Kind is SensorKind.Temperature or SensorKind.Power or SensorKind.Clock)
        || (item.Sensor is { } id && SensorMap.Find(snapshot, id) is { } sensor && SensorMap.ZeroIsMissing(sensor));

    // A CPU value the driver would read says so in its place, and the CPU's temperature and clock show it throttling.
    private static OverlayItem Marked(OverlayItem drawn, ItemConfig item, ValueHint hint, bool throttled, Snapshot snapshot, Texts texts) =>
        hint == ValueHint.NeedsDriver && ReadsWithTheDriver(item, snapshot) ? drawn with { Value = texts.Of(OverlayText.Driver) }
        : throttled && Throttling.Shows(item, snapshot) ? drawn with { Throttled = true }
        : drawn;

    private static ValueHint HintOf(Reading? reading, bool driverMissing, bool windowOpenedOnce) =>
        reading?.Usable is null ? (driverMissing ? ValueHint.NeedsDriver : ValueHint.ReadsNothing)
        : windowOpenedOnce ? ValueHint.None
        : ValueHint.SetUp;

    /// <summary>
    /// thermi's own tooltip, which is also its name for Narrator: the hottest value with its level, or why there is none,
    /// then what is hidden and whether click-through is on, as "thermi · CPU 78 °C, warning · Top right hidden ·
    /// click-through on", cut to Windows' limit.
    /// </summary>
    public static string Tooltip(ThermiConfig config, TrayInput input)
    {
        var texts = input.Texts;
        var parts = new List<string> { Name };
        if (Live(input) is not { } snapshot)
        {
            parts.Add(texts.Of(TrayText.SensorsStopped));
        }
        else if (Hottest(config, snapshot, input) is { } hottest)
        {
            parts.Add(hottest);
        }

        if (input.HideOverlays)
        {
            parts.Add(texts.Of(TrayText.OverlaysHidden));
        }
        else
        {
            var names = Names.OfOverlays(config, texts);
            parts.AddRange(config.Overlays
                .Select((overlay, at) => (overlay, at))
                .Where(each => each.overlay.Hidden)
                .Select(each => texts.Format(input.Numbers, TrayText.OverlayHidden, names[each.at])));
        }

        if (config.Overlays.Any(overlay => overlay.ClickThrough))
        {
            parts.Add(texts.Of(TrayText.ClickThroughOn));
        }

        return TrayTexts.Fit([string.Join(Separator, parts)]);
    }

    // The snapshot when it can be shown: fresh, from a service that runs.
    internal static Snapshot? Live(TrayInput input) => input.Service == ServiceState.Running ? input.Snapshot : null;

    // The value at the worst level, a temperature before any other and the hottest of them first, as its overlay shows it.
    private static string? Hottest(ThermiConfig config, Snapshot snapshot, TrayInput input)
    {
        var values = Enumerable.Range(0, config.Overlays.Count).SelectMany(overlay => Shown(config, overlay, snapshot, input)).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var (level, _, text) = values.OrderByDescending(value => value.Level).ThenByDescending(value => value.Heat).First();
        return level == Level.Normal ? text
            : input.Texts.Format(input.Numbers, TrayText.AtLevel, text, input.Texts.Of(level == Level.Critical ? TrayText.Critical : TrayText.Warning));
    }

    // Each item of an overlay that shows a value, with its level, its heat (a temperature's value, and below any of them
    // for the rest), and its label and value as the overlay shows them.
    private static IEnumerable<(Level Level, double Heat, string Text)> Shown(ThermiConfig config, int overlay, Snapshot snapshot, TrayInput input)
    {
        var items = config.Overlays[overlay].Items;
        var readings = SensorMap.Read(snapshot, config.Overlays[overlay]);
        var drawn = OverlayLayout.Build(config.Overlays[overlay], readings, input.Numbers, input.Texts, config.Temperature, input.History);
        var labels = OverlayLayout.RunLabels(items, input.Texts);
        for (var at = 0; at < items.Count; at++)
        {
            var item = items[at];
            if (OverlayLayout.ReadingOf(item, readings) is { Usable: { } value } reading && drawn.FirstOrDefault(each => each.Id == item.Id) is { } shown)
            {
                var heat = reading.Kind == SensorKind.Temperature ? value : double.MinValue;
                yield return (
                    OverlayLayout.LevelOf(item, OverlayLayout.MeterOf(item, readings)),
                    heat,
                    labels[at] is { } label ? string.Join(' ', label, shown.Value) : shown.Value);
            }
        }
    }
}
