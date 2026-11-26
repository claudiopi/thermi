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

    /// <summary>The menu entry that takes the last gesture back, while it is offered; null after that.</summary>
    public OverlayText? Offered => _last is { } last && time.GetElapsedTime(last.At) < Menus.GestureUndo ? last.Undo : null;

    /// <summary>
    /// A gesture written: offered for undo, named by <paramref name="undo"/> or else by its kind, and told to the window.
    /// </summary>
    public void Add(GestureKind kind, ThermiConfig before, ThermiConfig after, OverlayText? undo = null)
    {
        _last = new Done(undo ?? DesktopGestures.UndoOf(kind), before, after, time.GetTimestamp());
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

    private sealed record Done(OverlayText Undo, ThermiConfig Before, ThermiConfig After, long At);
}
