using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.App.Gestures;

/// <summary>
/// A drop that moves the overlay where it was let go: the press, the point its cursor was let go at for the overlay to keep,
/// and the drag distance a click stays within.
/// </summary>
internal readonly record struct PlainMove(Press Press, Position Released, DragDistance Distance);

/// <summary>
/// A desktop drag's drop written as one edit on every OS: settled by <c>settle</c>, which may refuse it; a plain move
/// through <see cref="Settings.Release"/>, so the overlay stays where it was dropped until written; and offered for undo.
/// </summary>
internal static class DropWrite
{
    /// <summary>
    /// Writes <paramref name="drop"/> of the overlay at <paramref name="overlay"/>, a plain move as <paramref name="move"/>
    /// says when it has one, offered for undo by its kind or as <paramref name="named"/>; true when the settings took it.
    /// </summary>
    public static bool Write(
        Settings settings,
        int overlay,
        DesktopDrop drop,
        Func<ThermiConfig, ThermiConfig, EditResult> settle,
        PlainMove? move,
        GestureUndo? undo,
        OverlayText? named = null)
    {
        var before = settings.Config;
        var after = drop.Result.Refused is null ? settle(before, drop.Result.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            return false;
        }

        // Undo takes back the very drop written, which a plain move takes from the cursor as the overlay does.
        if (move is { } plain && drop.Kind == GestureKind.Move && after.Overlays[overlay].Snap is null)
        {
            if (settings.Release(plain.Press, plain.Released, plain.Distance, overlay) is not { } released)
            {
                return false;
            }

            after = released;
        }
        else
        {
            settings.Edit(change);
        }

        undo?.Add(drop.Kind, before, after, named);
        return true;
    }
}
