using System.Drawing;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.App.Gestures;

/// <summary>What a press on an overlay took hold of: its edge, which moves it, a pill, or a floating overlay's corner.</summary>
internal enum Held
{
    Edge,
    Pill,
    Corner,
}

/// <summary>
/// Where a press on an overlay began: what it held, the pill or the item alone a hold takes, where its picture sits in the
/// overlay's pixels and the gap it leaves there once lifted, the overlay's corner on the screen, its size and scale.
/// </summary>
internal sealed record DragStart(Held Held, Carried? Carried, RectangleF PillBox, RectangleF Gap, Position Origin, Size Size, float Scale);

/// <summary>What a desktop shows of a drag, each OS its own way: the lifted pill carried as a picture, the snap marks, the pointer.</summary>
internal interface IDragShow : IDisposable
{
    /// <summary>Shows the pill or item the press lifted, its corner at <paramref name="at"/>: its size in pixels.</summary>
    Size Lift(Position at);

    /// <summary>Moves the lifted pill's corner to <paramref name="at"/>.</summary>
    void Carry(Position at);

    /// <summary>The snap marks of something <paramref name="size"/> big, under what is dragged: the lifted pill, or the overlay.</summary>
    IDragMarks Marks(IReadOnlyList<Screen> screens, Orientation orientation, Size size, bool lifted);

    /// <summary>The pointer says a drop does nothing here, or, false, that it does again.</summary>
    void Refuse(bool refused);

    /// <summary>Whether Alt is held now, which drops where the drag is, whatever mark or overlay is in reach.</summary>
    bool AltHeld();
}

/// <summary>A drag's snap marks on the screen: each spot it can snap to, the one a drop now snaps to filled.</summary>
internal interface IDragMarks : IDisposable
{
    void Light(SnapConfig? snap);
}

/// <summary>
/// One press on an overlay, from its press to its release, as a desktop gesture: the overlay moved over snap marks and
/// other overlays, a pill or an item held still until it lifts and then carried off as a picture of itself, or the
/// corner's size previewed. Its release writes one edit, settled by <c>settle</c>, which may refuse it, offered for undo
/// and told to the window; a click writes nothing.
/// </summary>
internal sealed class DesktopDrag(
    int overlay, Settings settings, DragStart start, IReadOnlyList<Screen> screens, IDragShow show, Func<ThermiConfig, ThermiConfig, EditResult> settle)
    : IDisposable
{
    // Pixels at 100 % a lifted pill rises above its place.
    private const int Rise = 4;

    private IDragMarks? _marks;
    private Size _pill;
    private bool _moved;
    private bool _crowded;

    /// <summary>Whether the drag moves the overlay itself, which a pill held or a corner held never do.</summary>
    public bool Moves => start.Held != Held.Corner && !Lifted && !Refused;

    public bool Resizes => start.Held == Held.Corner;

    public bool Lifted { get; private set; }

    /// <summary>Whether the pill was held long enough to lift but no other overlay fits, so the press does nothing.</summary>
    public bool Refused { get; private set; }

    /// <summary>Where the pill or item a hold takes leaves a gap in the overlay's pixels once lifted: an item alone, its pill's label kept.</summary>
    public RectangleF Gap => start.Gap;

    /// <summary>What a hold takes: the pill pressed, or the item alone, or nothing on the edge or the corner.</summary>
    public Carried? Carried => start.Carried;

    /// <summary>The size and direction a corner's drag shows until it is let go.</summary>
    public (double Size, Orientation Orientation)? Preview { get; private set; }

    /// <summary>Whether Alt is held, which the drop and the overlay it joins read too.</summary>
    public bool AltHeld() => show.AltHeld();

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
        if (_moved || Lifted || Refused || start.Carried is null || !DesktopGestures.Holds(settings.Config, overlay, start.Carried, held))
        {
            return;
        }

        if (!DesktopGestures.Lifts(settings.Config, overlay, start.Carried, held))
        {
            Refused = true;
            show.Refuse(true);
            return;
        }

        Lifted = true;
        var at = PillAt(start.Origin);
        _pill = show.Lift(at with { Y = at.Y - (int)(Rise * start.Scale) });
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
            show.Carry(PillAt(at));
            _marks ??= show.Marks(screens, Orientation.Horizontal, _pill, lifted: true);
            _marks.Light(Room(over ? null : Near(at), at));
            return false;
        }

        // Its first move paints the overlay again, with the background a drag gives it.
        var orientation = settings.Config.Overlays[overlay].Style.Orientation;
        var changed = _marks is null;
        _marks ??= show.Marks(screens, orientation, start.Size, lifted: false);
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
        return Snaps.Near(screens, orientation, size.Width, size.Height, corner, magnets: !show.AltHeld());
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
            show.Refuse(crowded);
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
    /// settle gives them, in the same edit, which <paramref name="undo"/> takes back. True when it wrote the settings.
    /// </summary>
    public bool Release(Press press, Position cursor, DragDistance distance, DropTarget target, GestureUndo? undo)
    {
        if (Dropped is not { } at)
        {
            return false;
        }

        var before = settings.Config;
        var drop = start.Held == Held.Corner
            ? new DesktopDrop(GestureKind.Resize, OverlayEdits.Resize(before, overlay, Preview!.Value.Size, Preview.Value.Orientation))
            : Drop(before, at, target, Near(at));
        return DropWrite.Write(settings, overlay, drop, settle, Moves ? new PlainMove(press, Kept(cursor, at), distance) : null, undo);
    }

    /// <summary>Where the overlay's corner was last dragged to, which its release drops at; null while still a click.</summary>
    public Position? Dropped { get; private set; }

    /// <summary>What the drag carries with the overlay's corner at <paramref name="dragged"/>, on the screen: the overlay, or a lifted pill.</summary>
    public PixelRect Box(Position dragged)
    {
        var (at, size) = Lifted ? (PillAt(dragged), _pill) : (dragged, start.Size);
        return new PixelRect(at.X, at.Y, at.X + size.Width, at.Y + size.Height);
    }

    public void Dispose()
    {
        _marks?.Dispose();
        show.Dispose();
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
}
