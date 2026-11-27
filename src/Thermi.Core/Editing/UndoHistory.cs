using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>
/// A step's name: the field it changed, and that field's value before and after it, as the file holds them (a number, a
/// switch or a name), or null where no value says it; a line's or an offset's values are readings of <see cref="Kind"/>.
/// </summary>
public sealed record StepName(ChangedField Field, object? Before, object? After, SensorKind? Kind = null);

/// <summary>The fields a step is named by, each a text of the window's; <see cref="Changes"/> names a step of several.</summary>
[Translated]
public enum ChangedField
{
    Warning,
    Critical,
    Thresholds,
    WarnBy,
    Graph,
    AlertOnly,
    NotifyMe,
    Sensor,
    Label,
    SeparatePill,
    TrayIcon,
    ValueColor,
    Offset,
    Shows,
    Smoothing,
    Decimals,
    Bits,
    ItemName,
    Place,
    ItemAdded,
    ItemRemoved,
    OverlayName,
    Position,
    Size,
    WhenToShow,
    Hidden,
    ClickThrough,
    Lock,
    OledCare,
    Fade,
    Look,
    Layout,
    Direction,
    Labels,
    TextSize,
    Opacity,
    Theme,
    Colors,
    Units,
    Font,
    Corners,
    Gap,
    TextEffect,
    OverlayAdded,
    OverlayRemoved,
    Merge,
    Hotkeys,
    Language,
    TemperatureUnit,
    WindowTheme,
    Agents,
    SensorName,
    Changes,
    PingHost,
}

/// <summary>The texts that tell a step: on the Undo and Redo buttons, and after an undo.</summary>
[Translated]
public enum UndoText
{
    /// <summary>The Undo button's tooltip, naming its step.</summary>
    Undo,

    /// <summary>The Redo button's tooltip, naming its step.</summary>
    Redo,

    /// <summary>A step named by its field and its value before and after, as "Critical 84 → 85 °C".</summary>
    Change,

    /// <summary>After an undo, a field that holds a value again.</summary>
    BackTo,

    /// <summary>After an undo, a switch that is on or off again.</summary>
    Again,

    /// <summary>After an undo of a step that has no value to name.</summary>
    Undone,

    /// <summary>After a redo, a field set to the value the step gave it, told as the change and not as a way back.</summary>
    RedoneTo,

    /// <summary>After a redo, a switch on or off as the step left it.</summary>
    RedoneAgain,

    /// <summary>After a redo of a step that has no value to name.</summary>
    Redone,

    /// <summary>A switch that is on, in <see cref="Again"/>.</summary>
    On,

    /// <summary>A step that adds or removes an item, with the item's name after it.</summary>
    Named,

    /// <summary>A switch that is off, in <see cref="Again"/>.</summary>
    Off,
}

/// <summary>What an undo or redo gave: the settings, and how many of the step's operations applied or were skipped.</summary>
public sealed record UndoResult(ThermiConfig Config, int Applied, int Skipped);

/// <summary>
/// The window's changes since it opened, as steps of operations on item ids, each with what it found and
/// what it wrote. A step takes back only what still holds what the window left there, so it never reverts
/// a hand edit or a drag, and redo checks the same way. A gesture, from press to release, is one step, and so
/// are repeated changes of one field within a second of each other, as a key held on a slider makes.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>How close repeated changes of one field come to be one step, as a key held on a slider makes them.</summary>
    public static readonly TimeSpan Repeat = TimeSpan.FromSeconds(1);

    private readonly Stack<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private readonly TimeProvider? _time;
    private bool _inGesture;
    private ThermiConfig? _gestureStart;
    private ThermiConfig? _gestureEnd;

    /// <summary>A history where every change is a step of its own.</summary>
    public UndoHistory()
    {
    }

    /// <summary>A history where repeated changes of one field within a second of <paramref name="time"/> are one step.</summary>
    public UndoHistory(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>What the next undo takes back, or null when there is nothing to undo.</summary>
    public StepName? UndoName => _undo.TryPeek(out var step) ? StepNames.Of(step.Operations) : null;

    /// <summary>What the next redo makes again, or null when there is nothing to redo.</summary>
    public StepName? RedoName => _redo.TryPeek(out var step) ? StepNames.Of(step.Operations) : null;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Keeps an edit the window made as one step, or as part of the gesture in progress. An edit that changed nothing is no step.</summary>
    public void Record(ThermiConfig before, ThermiConfig after)
    {
        if (_inGesture)
        {
            _gestureStart ??= before;
            _gestureEnd = after;
            return;
        }

        Keep(before, after, gesture: false);
    }

    // A gesture's step stands alone: no change before or after it within the second joins it.
    private void Keep(ThermiConfig before, ThermiConfig after, bool gesture)
    {
        var operations = ConfigDiff.Between(before, after);
        if (operations.Count == 0)
        {
            return;
        }

        _redo.Clear();
        var now = _time?.GetTimestamp() ?? 0;
        if (!gesture
            && _time is not null
            && _undo.TryPeek(out var last)
            && !last.Gesture
            && _time.GetElapsedTime(last.At, now) <= Repeat
            && Merged(last.Operations, operations) is { } merged)
        {
            _undo.Pop();
            if (merged.Count > 0)
            {
                _undo.Push(new Step(merged, now));
            }

            return;
        }

        _undo.Push(new Step(operations, now, gesture));
    }

    public void BeginGesture()
    {
        EndGesture();
        _inGesture = true;
    }

    /// <summary>Keeps every edit since the gesture began as one step, from what it found to what it left.</summary>
    public void EndGesture()
    {
        var (start, end) = (_gestureStart, _gestureEnd);
        (_inGesture, _gestureStart, _gestureEnd) = (false, null, null);
        if (start is not null && end is not null)
        {
            Keep(start, end, gesture: true);
        }
    }

    /// <summary>A saved style renamed: every step names it by its new name, so undo and redo never bring the old one back.</summary>
    public void RenameStyle(string from, string to)
    {
        EndGesture();
        Rename(_undo, from, to);
        Rename(_redo, from, to);
    }

    /// <summary>Takes the last step back from the settings as they are now, or gives null when there is none.</summary>
    public UndoResult? Undo(ThermiConfig current)
    {
        EndGesture();
        if (!_undo.TryPop(out var step))
        {
            return null;
        }

        _redo.Push(step);
        return Apply(current, step.Operations.Reverse(), operation => operation.Undo);
    }

    /// <summary>Puts back the step the last undo took, as when what it made was never written, so the next undo takes it again.</summary>
    public void CancelUndo()
    {
        if (_redo.TryPop(out var step))
        {
            _undo.Push(step);
        }
    }

    /// <summary>Puts back the step the last redo took, as when what it made was never written.</summary>
    public void CancelRedo()
    {
        if (_undo.TryPop(out var step))
        {
            _redo.Push(step);
        }
    }

    public UndoResult? Redo(ThermiConfig current)
    {
        EndGesture();
        if (!_redo.TryPop(out var step))
        {
            return null;
        }

        _undo.Push(step);
        return Apply(current, step.Operations, operation => operation.Redo);
    }

    // One field changed again from where the last step left it joins that step; changed back to where it started, it leaves no step.
    private static List<IOperation>? Merged(IReadOnlyList<IOperation> last, IReadOnlyList<IOperation> next) =>
        last is [var first] && next is [var second] && first.Then(second) is { } joined
            ? (joined.ChangesNothing ? [] : [joined])
            : null;

    // A stack enumerates from its top, so its steps are pushed back from the bottom.
    private static void Rename(Stack<Step> steps, string from, string to)
    {
        var renamed = steps.Reverse()
            .Select(step => step with { Operations = [.. step.Operations.Select(operation => operation.Renamed(from, to))] })
            .ToList();
        steps.Clear();
        foreach (var step in renamed)
        {
            steps.Push(step);
        }
    }

    // Each operation must also leave valid settings, or it is skipped: undo never writes what the window could not.
    private static UndoResult Apply(ThermiConfig config, IEnumerable<IOperation> operations, Func<IOperation, Func<ThermiConfig, ThermiConfig?>> direction)
    {
        var (applied, skipped) = (0, 0);
        foreach (var operation in operations)
        {
            if (direction(operation)(config) is { } changed && ConfigRules.Problem(changed) is null)
            {
                config = changed;
                applied++;
            }
            else
            {
                skipped++;
            }
        }

        return new UndoResult(config, applied, skipped);
    }

    /// <summary>A step's operations, and when its last change came, on the history's clock.</summary>
    private sealed record Step(IReadOnlyList<IOperation> Operations, long At, bool Gesture = false);
}
