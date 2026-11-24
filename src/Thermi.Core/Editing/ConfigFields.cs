using Thermi.Core.Settings;

namespace Thermi.Core.Editing;

/// <summary>One field of a part of the settings, read and written as a value, so undo can compare what it holds, and the name a step that changes it takes.</summary>
internal sealed record Field<T>(ChangedField Name, Func<T, object?> Get, Func<T, object?, T> Set);

/// <summary>
/// Every field undo tracks, and value equality for the settings: a file read again gives new lists, which
/// records compare by reference. A field added to the settings goes here too, or undo cannot take it back.
/// </summary>
internal static class ConfigFields
{
    private static readonly IReadOnlyList<ItemConfig> NoItems = [];

    /// <summary>The saved style a style came from, which a rename changes in the undo steps too.</summary>
    public static Field<OverlayConfig> StyleName { get; } = Style(ChangedField.Look, style => style.Name, (style, value) => style with { Name = (string?)value });

    /// <summary>An item's meter with what needs it, which a step names by what of it changed.</summary>
    public static Field<ItemConfig> ItemMetering { get; } = new(ChangedField.Meter, Metering.Of, (item, value) => ((Metering)value!).On(item));

    public static IReadOnlyList<Field<ThermiConfig>> Thermi { get; } =
    [
        new(ChangedField.Hotkeys, config => config.Hotkeys, (config, value) => config with { Hotkeys = (HotkeysConfig?)value }),
        new(ChangedField.Language, config => config.Language, (config, value) => config with { Language = (string?)value }),
        new(ChangedField.TemperatureUnit, config => config.Temperature, (config, value) => config with { Temperature = (TemperatureUnit)value! }),
        new(ChangedField.SensorName, config => config.SensorNames, (config, value) => config with { SensorNames = (IReadOnlyDictionary<string, string>?)value }),
    ];

    // Where an overlay sits is one choice, a snap or a free position: undoing one half after a drag set the other would move it.
    public static IReadOnlyList<Field<OverlayConfig>> Overlay { get; } =
    [
        new(ChangedField.Position, overlay => new Placement(overlay.Snap, overlay.Position), (overlay, value) => Placed(overlay, (Placement)value!)),
        new(ChangedField.Size, overlay => overlay.Size, (overlay, value) => overlay with { Size = (double)value! }),
        new(ChangedField.WhenToShow, overlay => overlay.Visibility, (overlay, value) => overlay with { Visibility = (Visibility)value! }),
        new(ChangedField.ClickThrough, overlay => overlay.ClickThrough, (overlay, value) => overlay with { ClickThrough = (bool)value! }),
        new(ChangedField.Lock, overlay => overlay.Locked, (overlay, value) => overlay with { Locked = (bool)value! }),
        new(ChangedField.OledCare, overlay => overlay.Oled, (overlay, value) => overlay with { Oled = (bool)value! }),
        new(ChangedField.Fade, overlay => overlay.Fade, (overlay, value) => overlay with { Fade = (bool)value! }),
        new(ChangedField.OverlayName, overlay => overlay.Name, (overlay, value) => overlay with { Name = (string?)value }),
        new(ChangedField.Hidden, overlay => overlay.Hidden, (overlay, value) => overlay with { Hidden = (bool)value! }),
        StyleName,
        Style(ChangedField.Layout, style => style.Layout, (style, value) => style with { Layout = (PillLayout)value! }),
        Style(ChangedField.Direction, style => style.Orientation, (style, value) => style with { Orientation = (Orientation)value! }),
        Style(ChangedField.Labels, style => style.NoLabels, (style, value) => style with { NoLabels = (bool)value! }),
        Style(ChangedField.TextSize, style => style.Preset, (style, value) => style with { Preset = (StylePreset)value! }),
        Style(ChangedField.TextSize, style => style.Label, (style, value) => style with { Label = (TextStyle?)value }),
        Style(ChangedField.TextSize, style => style.Value, (style, value) => style with { Value = (TextStyle?)value }),
        Style(ChangedField.Opacity, style => style.Opacity, (style, value) => style with { Opacity = (OpacityConfig)value! }),
        Style(ChangedField.Theme, style => style.Pills, (style, value) => style with { Pills = (PillTheme)value! }),
        Style(ChangedField.Colors, style => style.Colors, (style, value) => style with { Colors = (StyleColors?)value }),
        Style(ChangedField.Units, style => style.Units, (style, value) => style with { Units = (bool)value! }),
        Style(ChangedField.Font, style => style.Font, (style, value) => style with { Font = (StyleFont)value! }),
        Style(ChangedField.Corners, style => style.Corners, (style, value) => style with { Corners = (CornerStyle)value! }),
        Style(ChangedField.Gap, style => style.Gap, (style, value) => style with { Gap = (int)value! }),
        Style(ChangedField.TextEffect, style => style.Effect, (style, value) => style with { Effect = (TextEffect)value! }),
    ];

    // What an item shows is one choice, a metric, a sensor or combined sensors, with the metric it keeps as auto.
    // Its meter is another, with what needs it: undone one by one, a threshold or a graph would come back before its meter.
    public static IReadOnlyList<Field<ItemConfig>> Item { get; } =
    [
        new(ChangedField.Sensor, Source, (item, value) => WithSource(item, (ItemConfig)value!)),
        ItemMetering,
        new(ChangedField.Label, item => item.Label, (item, value) => item with { Label = (string?)value }),
        new(ChangedField.SeparatePill, item => item.Own, (item, value) => item with { Own = (bool)value! }),
        new(ChangedField.TrayIcon, item => item.Tray, (item, value) => item with { Tray = (bool)value! }),
        new(ChangedField.ValueColor, item => item.Color, (item, value) => item with { Color = (string?)value }),
        new(ChangedField.Offset, item => item.Offset, (item, value) => item with { Offset = (double)value! }),
        new(ChangedField.Shows, item => item.Stat, (item, value) => item with { Stat = (ItemStat)value! }),
        new(ChangedField.Smoothing, item => item.Smooth, (item, value) => item with { Smooth = (int)value! }),
        new(ChangedField.Decimals, item => item.Decimals, (item, value) => item with { Decimals = (int?)value }),
        new(ChangedField.Bits, item => item.Bits, (item, value) => item with { Bits = (bool)value! }),
        new(ChangedField.ItemName, item => item.Name, (item, value) => item with { Name = (string?)value }),
    ];

    public static bool Same(object? first, object? second) => first switch
    {
        ItemConfig item => second is ItemConfig other && Same(item, other),
        IReadOnlyDictionary<string, string> names => second is IReadOnlyDictionary<string, string> others && Same(names, others),
        _ => Equals(first, second),
    };

    public static bool Same(ItemConfig first, ItemConfig second) =>
        first with { Sensors = null } == second with { Sensors = null }
        && (first.Sensors is null) == (second.Sensors is null)
        && (first.Sensors ?? []).SequenceEqual(second.Sensors ?? [], StringComparer.Ordinal);

    public static bool Same(OverlayConfig first, OverlayConfig second) =>
        first with { Items = NoItems } == second with { Items = NoItems }
        && first.Items.Count == second.Items.Count
        && first.Items.Zip(second.Items).All(pair => Same(pair.First, pair.Second));

    private static bool Same(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var name) && name == pair.Value);

    private static Field<OverlayConfig> Style(ChangedField name, Func<StyleConfig, object?> get, Func<StyleConfig, object?, StyleConfig> set) =>
        new(name, overlay => get(overlay.Style), (overlay, value) => overlay with { Style = set(overlay.Style, value) });

    private static ItemConfig Source(ItemConfig item) =>
        new() { Metric = item.Metric, Sensor = item.Sensor, Sensors = item.Sensors, Combine = item.Combine, Auto = item.Auto };

    private static ItemConfig WithSource(ItemConfig item, ItemConfig source) =>
        item with { Metric = source.Metric, Sensor = source.Sensor, Sensors = source.Sensors, Combine = source.Combine, Auto = source.Auto };

    private static OverlayConfig Placed(OverlayConfig overlay, Placement placement) => overlay with { Snap = placement.Snap, Position = placement.Position };

    private sealed record Placement(SnapConfig? Snap, Position? Position);

    /// <summary>An item's meter with what needs it, as one field.</summary>
    internal sealed record Metering(MeterConfig? Meter, Threshold? Threshold, bool Graph, bool AlertOnly, bool Notify)
    {
        public static Metering Of(ItemConfig item) => new(item.Meter, item.Threshold, item.Graph, item.AlertOnly, item.Notify);

        public ItemConfig On(ItemConfig item) => item with { Meter = Meter, Threshold = Threshold, Graph = Graph, AlertOnly = AlertOnly, Notify = Notify };
    }
}
