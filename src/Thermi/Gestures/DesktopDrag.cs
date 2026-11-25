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
/// Where a press on an overlay began: what it held, the pill or the item alone a hold takes, where that sits in the
/// overlay's pixels, the overlay's corner on the screen, its size and scale, and its own picture, which it carries lifted.
/// </summary>
internal sealed record DragStart(Held Held, Carried? Carried, RectangleF PillBox, Position Origin, Size Size, float Scale, Func<Bitmap>? PillPicture);

/// <summary>
/// One press on an overlay, from its capture to its release, as a desktop gesture: the overlay moved over snap marks and
/// other overlays, a pill or an item held still until it lifts and then carried off as a picture of itself, or the
/// corner's size previewed. Its release writes one edit, settled by <c>settle</c>, which may refuse it, offered for undo
/// and told to the window; a click writes nothing.
/// </summary>
internal sealed class DesktopDrag(
    int overlay, Settings settings, DragStart start, IReadOnlyList<Screen> screens, HWND window, Func<ThermiConfig, ThermiConfig, EditResult> settle)
    : IDisposable
{
    // Pixels at 100 % a lifted pill rises above its place.
    private const int Rise = 4;

    private SnapMarks? _marks;
    private HWND _ghost;
    private Size _pill;
    private bool _moved;
    private bool _crowded;

    /// <summary>Whether the drag moves the overlay itself, which a pill held or a corner held never do.</summary>
    public bool Moves => start.Held != Held.Corner && !Lifted && !Refused;

    public bool Resizes => start.Held == Held.Corner;

    public bool Lifted { get; private set; }

    /// <summary>Whether the pill was held long enough to lift but no other overlay fits, so the press does nothing.</summary>
    public bool Refused { get; private set; }

    /// <summary>Where the pill or item a hold takes sits in the overlay's pixels, which it leaves as a gap once lifted.</summary>
    public RectangleF PillBox => start.PillBox;

    /// <summary>What a hold takes: the pill pressed, or the item alone, or nothing on the edge or the corner.</summary>
    public Carried? Carried => start.Carried;

    /// <summary>The size and direction a corner's drag shows until it is let go.</summary>
    public (double Size, Orientation Orientation)? Preview { get; private set; }

    /// <summary>The overlay as the drag shows it: at its previewed size and direction while a corner is held.</summary>
    public OverlayConfig Shown(OverlayConfig shown) =>
        Preview is var (size, orientation) ? shown with { Size = size, Style = shown.Style with { Orientation = orientation } } : shown;

    /// <summary>
    /// The press has held still for <paramref name="held"/>: a pill or an item that can come off lifts, a little above its
    /// place, so the drag that follows carries it instead of the overlay. Nothing lifts once the press has left the drag distance,
    /// even back inside it, since a slow drag is how a person moves the overlay. At the cap of overlays it lifts nothing and
    /// the press is refused, which the pointer shows until it is let go.
    /// </summary>
    public void Lift(TimeSpan held)
    {
        if (_moved || Lifted || Refused || start.PillPicture is not { } picture || !DesktopGestures.Holds(settings.Config, overlay, start.Carried, held))
        {
            return;
        }

        if (!DesktopGestures.Lifts(settings.Config, overlay, start.Carried, held))
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
            _marks.Light(Room(over ? null : Near(at), at));
            return false;
        }

        // Its first move paints the overlay again, with the background a drag gives it.
        var orientation = settings.Config.Overlays[overlay].Style.Orientation;
        var changed = _marks is null;
        _marks ??= new SnapMarks(screens, orientation, start.Size, start.Scale, window);
        // Over another overlay the drop merges, wherever a snap would be, so no mark is lit.
        _marks.Light(Room(over ? null : Near(at), at));
        return changed;
    }

    // The mark in reach of what the drag carries, the overlay or a lifted pill, with the overlay's corner at at. Alt held
    // as the move or the drop was sent drops it where it is, whatever mark is in reach.
    private SnapConfig? Near(Position at)
    {
        var (corner, orientation, size) = Lifted || Refused
            ? (PillAt(at), Orientation.Horizontal, _pill)
            : (at, settings.Config.Overlays[overlay].Style.Orientation, start.Size);
        return Snaps.Near(screens, orientation, size.Width, size.Height, corner, magnets: !AltHeld());
    }

    // A place on a taskbar whose drop the settle refuses, as the others leave no room there for what the drag carries,
    // lights no mark, and the pointer says so until the drag leaves it: the drop is asked as the release asks it.
    private SnapConfig? Room(SnapConfig? near, Position at)
    {
        var before = settings.Config;
        var crowded = near is { Spot: SnapSpot.Taskbar }
            && Drop(before, at, default, near) is { Result.Refused: null } drop
            && settle(before, drop.Result.Config).Refused == Refusal.NoRoomOnTaskbar;
        if (crowded != _crowded)
        {
            _crowded = crowded;
            PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, crowded ? PInvoke.IDC_NO : PInvoke.IDC_ARROW));
        }

        return crowded ? null : near;
    }

    private DesktopDrop Drop(ThermiConfig before, Position at, DropTarget target, SnapConfig? mark) =>
        Lifted || Refused
            ? DesktopGestures.Release(before, overlay, start.Carried, Kept(PillAt(at), _pill), target, mark)
            : DesktopGestures.Release(before, overlay, null, Kept(at, start.Size), target, mark);

    /// <summary>
    /// The press let go: a drag writes its gesture as one edit, the plain move through <see cref="Settings.Release"/> so it
    /// stays where it was dropped until written. The overlays it pushes along a taskbar keep their new places, as the
    /// settle gives them, in the same edit, which <paramref name="undo"/> takes back.
    /// </summary>
    public void Release(Press press, Position cursor, DragDistance distance, DropTarget target, GestureUndo? undo)
    {
        if (Dropped is not { } at)
        {
            return;
        }

        var before = settings.Config;
        var drop = start.Held == Held.Corner
            ? new DesktopDrop(GestureKind.Resize, OverlayEdits.Resize(before, overlay, Preview!.Value.Size, Preview.Value.Orientation))
            : Drop(before, at, target, Near(at));
        var after = drop.Result.Refused is null ? settle(before, drop.Result.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            return;
        }

        // Undo takes back the very drop written, which a plain move takes from the cursor as the overlay does.
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
