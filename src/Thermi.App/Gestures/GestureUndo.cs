using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.App.Gestures;

/// <summary>
/// The last desktop gesture or edit chosen from a menu, which every overlay's menu offers to take back for
/// <see cref="Menus.GestureUndo"/>, and which the open window takes as a step of its own history, as it was told.
/// </summary>
internal sealed class GestureUndo(TimeProvider time, Action<Gesture> tell)
{
    private Done? _last;

    /// <summary>
    /// The menu entry that takes the last gesture back, while it is offered and <paramref name="settings"/> still hold some
    /// of what it wrote; null once taking it back would change nothing, as after the window's own undo took it back.
    /// </summary>
    public OverlayText? Offered(ThermiConfig settings) =>
        _last is { } last && time.GetElapsedTime(last.At) < Menus.GestureUndo
            && ConfigEdits.Replay(last.After, last.Before) is { } back && ConfigEdits.Replay(settings, back(settings)) is not null
            ? last.Undo
            : null;

    /// <summary>
    /// A gesture written: offered for undo, named by <paramref name="undo"/> or else by its kind, and told to the window.
    /// One that <paramref name="joins"/> the last, of its kind, from where that left the settings and within
    /// <see cref="UndoHistory.Repeat"/>, is one step with it, as the window's history makes of them.
    /// </summary>
    public void Add(GestureKind kind, ThermiConfig before, ThermiConfig after, OverlayText? undo = null, bool joins = false)
    {
        var now = time.GetTimestamp();
        var from = joins && _last is { } last && last.Kind == kind && time.GetElapsedTime(last.At, now) <= UndoHistory.Repeat
            && ConfigEdits.Replay(last.After, before) is null
                ? last.Before
                : before;
        _last = new Done(kind, undo ?? DesktopGestures.UndoOf(kind), from, after, now);
        tell(new Gesture(kind, ThermiConfigJson.Write(before), ThermiConfigJson.Write(after)));
    }

    /// <summary>
    /// The change that takes the last gesture back, where the settings still hold what it wrote, so a later change made
    /// elsewhere stays; it is offered once. Null when there is none to take back.
    /// </summary>
    public Func<ThermiConfig, ThermiConfig>? Take()
    {
        var last = _last;
        _last = null;
        return last is null ? null : ConfigEdits.Replay(last.After, last.Before);
    }

    private sealed record Done(GestureKind Kind, OverlayText Undo, ThermiConfig Before, ThermiConfig After, long At);
}
