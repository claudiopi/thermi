using System.Drawing;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Painting;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Gestures;

/// <summary>What a press on an overlay took hold of: its edge, which moves it, a pill, or a floating overlay's corner.</summary>
internal enum Held
{
    Edge,
    Pill,
    Corner,
}

/// <summary>
/// Where a press on an overlay began: what it held, the item of a pill held, where that pill sits in the overlay's
/// pixels, the overlay's corner on the screen, its size and scale, and a pill's own picture, which a pill lifted carries.
/// </summary>
internal sealed record DragStart(Held Held, string? Pill, RectangleF PillBox, Position Origin, Size Size, float Scale, Func<Bitmap>? PillPicture);

/// <summary>Where a drag was let go: the other overlay it is over, and the item of it before whose pill a pill joins.</summary>
internal readonly record struct DropTarget(int? Onto, string? Before);

/// <summary>
/// One press on an overlay, from its capture to its release, as a desktop gesture: the overlay moved over snap marks and
/// other overlays, a pill held still until it lifts and then carried off as a picture of itself, or the corner's size
/// previewed. Its release writes one edit, offered for undo and told to the window; a click writes nothing.
/// </summary>
internal sealed class DesktopDrag(int overlay, Settings settings, DragStart start, IReadOnlyList<Screen> screens, HWND window) : IDisposable
{
    // Pixels at 100 % a lifted pill rises above its place.
    private const int Rise = 4;

    private SnapMarks? _marks;
    private HWND _ghost;
    private Size _pill;
    private bool _moved;

    /// <summary>Whether the drag moves the overlay itself, which a pill held or a corner held never do.</summary>
    public bool Moves => start.Held != Held.Corner && !Lifted && !Refused;

    public bool Resizes => start.Held == Held.Corner;

    public bool Lifted { get; private set; }

    /// <summary>Whether the pill was held long enough to lift but no other overlay fits, so the press does nothing.</summary>
    public bool Refused { get; private set; }

    /// <summary>Where the pill pressed sits in the overlay's pixels, which a lifted pill leaves as a gap.</summary>
    public RectangleF PillBox => start.PillBox;

    /// <summary>The size and direction a corner's drag shows until it is let go.</summary>
    public (double Size, Orientation Orientation)? Preview { get; private set; }

    /// <summary>The overlay as the drag shows it: at its previewed size and direction while a corner is held.</summary>
    public OverlayConfig Shown(OverlayConfig shown) =>
        Preview is var (size, orientation) ? shown with { Size = size, Style = shown.Style with { Orientation = orientation } } : shown;

    /// <summary>
    /// The press has held still for <paramref name="held"/>: a pill that can come off lifts, a little above its place, so
    /// the drag that follows carries it instead of the overlay. Nothing lifts once the press has left the drag distance,
    /// even back inside it, since a slow drag is how a person moves the overlay. At the cap of overlays it lifts nothing and
    /// the press is refused, which the pointer shows until it is let go.
    /// </summary>
    public void Lift(TimeSpan held)
    {
        if (_moved || Lifted || Refused || start.PillPicture is not { } picture || !DesktopGestures.Holds(settings.Config, overlay, start.Pill, held))
        {
            return;
        }

        if (!DesktopGestures.Lifts(settings.Config, overlay, start.Pill, held))
        {
            Refused = true;
            PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_NO));
            return;
        }

        Lifted = true;
        _ghost = TopLevelWindow.Create(OverlayWindow.ClassName, OverlayWindow.ExStyle | WINDOW_EX_STYLE.WS_EX_TRANSPARENT, WINDOW_STYLE.WS_POPUP);
        using var bitmap = picture();
        _pill = bitmap.Size;
        var at = PillAt(start.Origin);
        LayeredPicture.ShowOnce(_ghost, bitmap, at with { Y = at.Y - (int)(Rise * start.Scale) });
        PInvoke.ShowWindow(_ghost, SHOW_WINDOW_CMD.SW_SHOWNA);
    }

    /// <summary>
    /// The press moved, with the overlay's corner where the drag has it, or null while still a click, and
    /// <paramref name="over"/> another overlay; true when the overlay must paint again.
    /// </summary>
    public bool Moved(Position? dragged, bool over = false)
    {
        Dropped = dragged;
        if (dragged is not { } at)
        {
            return false;
        }

        _moved = true;
        if (Refused)
        {
            return false;
        }

        if (start.Held == Held.Corner)
        {
            var size = settings.Config.Overlays[overlay].Size;
            Preview = DesktopGestures.Corner(size, start.Size.Width, start.Size.Height, at.X - start.Origin.X, at.Y - start.Origin.Y);
            return true;
        }

        if (Lifted)
        {
            Carry(PillAt(at));
            _marks ??= new SnapMarks(screens, Orientation.Horizontal, _pill, start.Scale, _ghost);
            _marks.Light(over ? null : Near(PillAt(at), Orientation.Horizontal, _pill));
            return false;
        }

        // Its first move paints the overlay again, with the background a drag gives it.
        var orientation = settings.Config.Overlays[overlay].Style.Orientation;
        var changed = _marks is null;
        _marks ??= new SnapMarks(screens, orientation, start.Size, start.Scale, window);
        // Over another overlay the drop merges, wherever a snap would be, so no mark is lit.
        _marks.Light(over ? null : Near(at, orientation, start.Size));
        return changed;
    }

    // Alt held as the move or the drop was sent drops it where it is, whatever mark is in reach.
    private SnapConfig? Near(Position at, Orientation orientation, Size size) =>
        Snaps.Near(screens, orientation, size.Width, size.Height, at, magnets: !AltHeld());

    /// <summary>
    /// The press let go: a drag writes its gesture as one edit, the plain move through <see cref="Settings.Release"/> so it
    /// stays where it was dropped until written.
    /// </summary>
    public void Release(Press press, Position cursor, DragDistance distance, DropTarget target, GestureUndo? undo)
    {
        if (Dropped is not { } at)
        {
            return;
        }

        var before = settings.Config;
        var pill = PillAt(at);
        var drop = start.Held == Held.Corner
            ? new DesktopDrop(GestureKind.Resize, OverlayEdits.Resize(before, overlay, Preview!.Value.Size, Preview.Value.Orientation))
            : Lifted || Refused
                ? DesktopGestures.Release(before, overlay, start.Pill, Kept(pill, _pill), target.Onto, Near(pill, Orientation.Horizontal, _pill), target.Before)
                : DesktopGestures.Release(
                    before, overlay, null, Kept(at, start.Size), target.Onto, Near(at, before.Overlays[overlay].Style.Orientation, start.Size), target.Before);
        if (drop.Result.Refused is not null || ConfigEdits.Replay(before, drop.Result.Config) is not { } change)
        {
            return;
        }

        // Undo takes back the very drop written, which a plain move takes from the cursor as the overlay does.
        var after = drop.Result.Config;
        if (Moves && drop.Kind == GestureKind.Move && after.Overlays[overlay].Snap is null)
        {
            if (settings.Release(press, Kept(cursor, at), distance, overlay) is not { } released)
            {
                return;
            }

            after = released;
        }
        else
        {
            settings.Edit(change);
        }

        undo?.Add(drop.Kind, before, after);
    }

    /// <summary>Where the overlay's corner was last dragged to, which its release drops at; null while still a click.</summary>
    public Position? Dropped { get; private set; }

    /// <summary>What the drag carries with the overlay's corner at <paramref name="dragged"/>, on the screen: the overlay, or a lifted pill.</summary>
    public PixelRect Box(Position dragged)
    {
        var (at, size) = Lifted ? (PillAt(dragged), _pill) : (dragged, start.Size);
        return new PixelRect(at.X, at.Y, at.X + size.Width, at.Y + size.Height);
    }

    /// <summary>Whether Alt was down as the message being handled was sent.</summary>
    public static bool AltHeld() => PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_MENU) < 0;

    public static IReadOnlyList<PixelRect?> Boxes(IReadOnlyList<OverlayWindow> overlays) =>
        [.. overlays.Select(overlay => PInvoke.IsWindowVisible(overlay.Hwnd) && PInvoke.GetWindowRect(overlay.Hwnd, out var rect)
            ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom)
            : (PixelRect?)null)];

    public void Dispose()
    {
        _marks?.Dispose();
        if (!_ghost.IsNull)
        {
            PInvoke.DestroyWindow(_ghost);
        }
    }

    // A drop partly off its monitor's work area is written where it then shows, inside it.
    private Position Kept(Position at, Size size) => Snaps.Inside(at, screens, size.Width, size.Height);

    // The cursor as it would have been let go with the overlay where it is kept.
    private Position Kept(Position cursor, Position at)
    {
        var kept = Kept(at, start.Size);
        return new(cursor.X + kept.X - at.X, cursor.Y + kept.Y - at.Y);
    }

    private Position PillAt(Position at) => new(at.X + (int)start.PillBox.Left, at.Y + (int)start.PillBox.Top);

    private void Carry(Position at) =>
        PInvoke.SetWindowPos(
            _ghost,
            HWND.Null,
            at.X,
            at.Y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
}
