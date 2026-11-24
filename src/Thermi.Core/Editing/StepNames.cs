using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// What an undo step is called: the one field it changed with its value before and after, as the file holds them, and
/// the type of what a line or an offset is a value of, so <see cref="StepTexts"/> can say "Critical 84 → 85 °C"; a resize
/// is its size, items taken to another overlay their merge or move, and any other step of several fields
/// <see cref="ChangedField.Changes"/>.
/// </summary>
internal static class StepNames
{
    public static StepName Of(IReadOnlyList<IOperation> operations)
    {
        // The items a sensor's rename carries along are part of it, not changes of their own.
        var names = operations.Select(operation => operation.Name).ToList();
        var fields = names.Select(name => name.Field).Distinct().ToList();
        return names.Count == 1 ? names[0]
            : names.Find(name => name.Field == ChangedField.SensorName) is { } renamed ? renamed
            : fields.Count == 1 ? new StepName(names[0].Field, null, null)
            : fields is [ChangedField.Size, ChangedField.Direction] or [ChangedField.Direction, ChangedField.Size]
                ? new StepName(ChangedField.Size, null, null)
            : Moved(operations) ?? new StepName(ChangedField.Changes, null, null);
    }

    public static StepName Of(ChangedField field, object? before, object? after, SensorKind? kind = null) => new(field, Value(before), Value(after), kind);

    // Items that only went to another overlay: into one that went away is a merge, into a new one a new overlay, and
    // into one that stays a move.
    private static StepName? Moved(IReadOnlyList<IOperation> operations)
    {
        var (gone, come) = (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var (added, removed) = (false, false);
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case ItemPresence item:
                    (item.Added ? come : gone).Add(item.Item.Id);
                    break;
                case OverlayPresence overlay:
                    (overlay.Added ? come : gone).UnionWith(overlay.Overlay.Items.Select(item => item.Id));
                    (added, removed) = (added || overlay.Added, removed || !overlay.Added);
                    break;
                case ItemMove:
                    break;
                default:
                    return null;
            }
        }

        var field = removed ? ChangedField.Merge : added ? ChangedField.OverlayAdded : ChangedField.Place;
        return gone.SetEquals(come) ? new StepName(field, null, null) : null;
    }

    /// <summary>A meter's change is named by what of it changed: the meter, a line, both lines, the graph, alert-only or the notice.</summary>
    public static StepName OfMetering(ConfigFields.Metering before, ConfigFields.Metering after)
    {
        var (was, now) = (Lines(before.Threshold), Lines(after.Threshold));
        var kind = after.Meter is { } meter ? KindOf(meter.Metric ?? meter.Auto, meter.Sensor) : null;
        return (Equals(before.Meter, after.Meter), Equals(was.Warning, now.Warning), Equals(was.Critical, now.Critical)) switch
        {
            (false, _, _) => new StepName(ChangedField.Meter, null, null),
            (_, false, false) => new StepName(ChangedField.Thresholds, null, null),
            (_, false, true) => Of(ChangedField.Warning, was.Warning, now.Warning, kind),
            (_, true, false) => Of(ChangedField.Critical, was.Critical, now.Critical, kind),
            _ => OfSwitch(before, after),
        };
    }

    /// <summary>What an item reads, from its metric or its first sensor's id, so a step is named in its unit even while the service is not read.</summary>
    public static SensorKind? KindOf(ItemConfig item) => KindOf(item.Metric ?? item.Auto, item.Sensor ?? (item.Sensors is [var first, ..] ? first : null));

    private static SensorKind? KindOf(string? metric, string? sensor) =>
        Metrics.Find(metric)?.Kind ?? (sensor is null ? null : SensorMap.KindOfId(sensor));

    private static StepName OfSwitch(ConfigFields.Metering before, ConfigFields.Metering after) =>
        before.Graph != after.Graph ? Of(ChangedField.Graph, before.Graph, after.Graph)
        : before.AlertOnly != after.AlertOnly ? Of(ChangedField.AlertOnly, before.AlertOnly, after.AlertOnly)
        : Of(ChangedField.NotifyMe, before.Notify, after.Notify);

    private static (double? Warning, double? Critical) Lines(Threshold? threshold) => threshold is null ? default : (threshold.Warning, threshold.Critical);

    // A value is named as the file holds it; a choice or a group of fields has no single value to name.
    // A metric's name stands for an item that stores none, named in the window's language when the step is told.
    private static object? Value(object? value) => value is double or int or bool or string or MetricName ? value : null;
}

/// <summary>
/// The texts that tell a step, in the window's language and unit: "Critical 84 → 85 °C" on the Undo and Redo buttons, and
/// "Undone: Critical back to 84 °C." or "Undone: Separate pill is Off again." after an undo. A switch, a place, or a
/// value that only one side has, is named by its field alone.
/// </summary>
public static class StepTexts
{
    /// <summary>The step as the Undo or Redo button names it in its tooltip, <paramref name="button"/> being one of the two.</summary>
    public static string Button(UndoText button, StepName step, ValueFormat format) => format.Texts.Format(format.Numbers, button, Of(step, format));

    public static string Of(StepName step, ValueFormat format) =>
        Shown(step, step.Before, format, units: false) is { } before && Shown(step, step.After, format, units: true) is { } after
            ? format.Texts.Format(format.Numbers, UndoText.Change, Field(step, format), before, after)
            : Field(step, format);

    /// <summary>What an undo of the step says it changed back.</summary>
    public static string Undone(StepName step, ValueFormat format) =>
        Told(step, step.Before, step.After, format, (UndoText.Again, UndoText.BackTo, UndoText.Undone));

    /// <summary>What a redo of the step says it did again.</summary>
    public static string Redone(StepName step, ValueFormat format) =>
        Told(step, step.After, step.Before, format, (UndoText.RedoneAgain, UndoText.RedoneTo, UndoText.Redone));

    // The value the step leaves now, against the one it leaves behind, told in the undo's or the redo's words.
    private static string Told(StepName step, object? now, object? left, ValueFormat format, (UndoText Again, UndoText To, UndoText Plain) words) =>
        now is bool on && step.Field != ChangedField.Hidden
            ? format.Texts.Format(format.Numbers, words.Again, Field(step, format), format.Texts.Of(on ? UndoText.On : UndoText.Off))
        : Shown(step, left, format, units: true) is not null && Shown(step, now, format, units: true) is { } back
            ? format.Texts.Format(format.Numbers, words.To, Field(step, format), back)
        : format.Texts.Format(format.Numbers, words.Plain, Field(step, format));

    // Hiding an overlay is named as its button does it, an action and not a switch, so showing it again is the other button.
    private static string Field(StepName step, ValueFormat format) =>
        step is { Field: ChangedField.Hidden, After: false } ? format.Texts.Of(UiText.ShowThisOverlay) : format.Texts.Of(step.Field);

    // A line or an offset in its reading's unit; any other number in the regional format, and a name as it is.
    private static string? Shown(StepName step, object? value, ValueFormat format, bool units) => value switch
    {
        double number when step.Kind is { } kind => ReadingFormatter.Format(kind, number, format, units, difference: step.Field == ChangedField.Offset),
        // The size is a factor in the file and a percent on its slider.
        double size when step.Field == ChangedField.Size => Math.Round(size * 100).ToString(format.Numbers) + (units ? " %" : ""),
        double number => number.ToString(format.Numbers),
        int number => number.ToString(format.Numbers),
        string text => text,
        MetricName name => format.Texts.Of(name),
        _ => null,
    };
}
